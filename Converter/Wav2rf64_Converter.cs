using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2rf64_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var wavFiles = Directory.GetFiles(directoryPath, "*.wav", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    var match = Regex.Match(Path.GetFileNameWithoutExtension(f), @"_(\d+)$");
                    return match.Success && int.TryParse(match.Groups[1].Value, out int num) ? num : int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = wavFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var filePath in wavFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.wav");
                    var rf64Path = Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, $"{fileName}.rf64");

                    try
                    {
                        if (File.Exists(rf64Path)) File.Delete(rf64Path);
                        var ok = await Task.Run(() => ConvertWavToRf64(filePath, rf64Path), cancellationToken);

                        if (ok && File.Exists(rf64Path))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(rf64Path)}");
                            OnFileConverted(rf64Path);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.wav转换失败");
                            OnConversionFailed($"{fileName}.wav转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.wav处理错误:{ex.Message}");
                    }
                }

                ConversionProgress?.Invoke(this, successCount > 0
                    ? $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件"
                    : "转换完成,但未成功转换任何文件");

                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }

        private bool ConvertWavToRf64(string wavPath, string rf64Path)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取WAV文件:{Path.GetFileName(wavPath)}");

                using var fs = File.OpenRead(wavPath);
                using var br = new BinaryReader(fs);

                var riff = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (riff != "RIFF") { ConversionError?.Invoke(this, "不是有效的WAV文件"); return false; }

                br.ReadUInt32();
                var wave = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (wave != "WAVE") { ConversionError?.Invoke(this, "不是有效的WAV文件"); return false; }

                Dictionary<string, byte[]> chunks = new Dictionary<string, byte[]>();
                byte[] fmtData = Array.Empty<byte>();
                long dataStart = 0;
                uint dataSize = 0;

                while (fs.Position < fs.Length)
                {
                    var chunkId = Encoding.ASCII.GetString(br.ReadBytes(4));
                    var chunkSize = br.ReadUInt32();
                    var pos = fs.Position;

                    if (chunkId == "fmt ")
                    {
                        fmtData = br.ReadBytes((int)chunkSize);
                        chunks[chunkId] = fmtData;
                    }
                    else if (chunkId == "data")
                    {
                        dataStart = fs.Position;
                        dataSize = chunkSize;
                        break;
                    }
                    else
                    {
                        chunks[chunkId] = br.ReadBytes((int)chunkSize);
                    }

                    if (chunkSize % 2 != 0) fs.ReadByte();
                }

                if (fmtData.Length == 0) { ConversionError?.Invoke(this, "WAV文件缺少fmt chunk"); return false; }

                var format = BitConverter.ToUInt16(fmtData, 0);
                if (format != 1) { ConversionError?.Invoke(this, $"只支持PCM格式,当前类型:{format}"); return false; }

                var channels = BitConverter.ToUInt16(fmtData, 2);
                var sampleRate = BitConverter.ToUInt32(fmtData, 4);
                var blockAlign = BitConverter.ToUInt16(fmtData, 12);
                var bits = BitConverter.ToUInt16(fmtData, 14);

                ConversionProgress?.Invoke(this, $"声道数:{channels},采样率:{sampleRate},比特率:{bits}");

                using var outFs = File.Create(rf64Path);
                using var bw = new BinaryWriter(outFs);

                bw.Write(Encoding.ASCII.GetBytes("RF64"));
                bw.Write(0xFFFFFFFFu);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                bw.Write(Encoding.ASCII.GetBytes("ds64"));
                bw.Write(20u);
                var ds64Pos = outFs.Position;
                bw.Write(0uL);
                bw.Write(0uL);
                bw.Write(0uL);
                bw.Write(0u);

                foreach (var c in chunks)
                {
                    bw.Write(Encoding.ASCII.GetBytes(c.Key));
                    bw.Write((uint)c.Value.Length);
                    bw.Write(c.Value);
                    if (c.Value.Length % 2 != 0) bw.Write((byte)0);
                }

                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(0xFFFFFFFFu);

                fs.Seek(dataStart, SeekOrigin.Begin);
                byte[] buffer = new byte[8192];
                long total = 0;
                var start = DateTime.Now;
                var cts = new CancellationTokenSource();

                var monitor = Task.Run(() =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        Thread.Sleep(10000);
                        if (cts.Token.IsCancellationRequested) break;
                        var speed = total / (DateTime.Now - start).TotalSeconds;
                        ConversionProgress?.Invoke(this, $"复制数据块...[已复制 {Humanize((ulong)total)}, 平均速度 {Humanize((ulong)speed)}/s]");
                    }
                });

                var remaining = (long)dataSize;
                while (remaining > 0)
                {
                    var read = fs.Read(buffer, 0, Math.Min(buffer.Length, (int)Math.Min(remaining, int.MaxValue)));
                    if (read == 0) break;
                    bw.Write(buffer, 0, read);
                    total += read;
                    remaining -= read;
                }

                cts.Cancel();
                monitor.Wait();
                ConversionProgress?.Invoke(this, "完成复制");

                var samples = (ulong)total / blockAlign;
                var duration = (double)samples / sampleRate;
                ConversionProgress?.Invoke(this, $"RF64包含{samples}个样本({blockAlign}字节每样本),时长{duration:F2}s");

                outFs.Seek(ds64Pos, SeekOrigin.Begin);
                bw.Write((ulong)outFs.Length);
                bw.Write((ulong)total);
                bw.Write(samples);

                return true;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }

        private string Humanize(ulong size)
        {
            if (size < 10) return $"{size} B";
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            var exp = Math.Min((int)Math.Floor(Math.Log(size, 1024)), units.Length - 1);
            var val = Math.Round(size / Math.Pow(1024, exp), 1);
            return val < 10 ? $"{val:F1} {units[exp]}" : $"{val:F0} {units[exp]}";
        }
    }
}