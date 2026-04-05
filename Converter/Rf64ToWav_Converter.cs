using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Rf64ToWav_Converter : BaseExtractor
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

            var rf64Files = Directory.GetFiles(directoryPath, "*.rf64", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    var match = Regex.Match(Path.GetFileNameWithoutExtension(f), @"_(\d+)$");
                    return match.Success && int.TryParse(match.Groups[1].Value, out int num) ? num : int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = rf64Files.Length;
            int successCount = 0;

            try
            {
                foreach (var filePath in rf64Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.rf64");
                    var wavPath = Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavPath)) File.Delete(wavPath);
                        var ok = await Task.Run(() => ConvertRf64ToWav(filePath, wavPath), cancellationToken);

                        if (ok && File.Exists(wavPath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavPath)}");
                            OnFileConverted(wavPath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.rf64转换失败");
                            OnConversionFailed($"{fileName}.rf64转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.rf64处理错误:{ex.Message}");
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

        private bool ConvertRf64ToWav(string rf64Path, string wavPath)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取RF64文件:{Path.GetFileName(rf64Path)}");

                using var fs = File.OpenRead(rf64Path);
                using var br = new BinaryReader(fs);

                var signature = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (signature != "RF64") { ConversionError?.Invoke(this, "不是有效的RF64文件"); return false; }

                br.ReadUInt32();
                var wave = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (wave != "WAVE") { ConversionError?.Invoke(this, "不是有效的WAVE文件"); return false; }

                Dictionary<string, byte[]> chunks = new Dictionary<string, byte[]>();
                byte[] fmtData = Array.Empty<byte>();
                long dataStart = 0;
                ulong dataSize64 = 0;

                while (fs.Position < fs.Length)
                {
                    if (fs.Position + 8 > fs.Length) break;
                    var chunkId = Encoding.ASCII.GetString(br.ReadBytes(4));
                    var chunkSize = br.ReadUInt32();
                    long chunkPos = fs.Position;

                    if (chunkId == "ds64")
                    {
                        br.ReadUInt64();
                        dataSize64 = br.ReadUInt64();
                        br.ReadUInt64();
                        br.ReadUInt32();
                    }
                    else if (chunkId == "fmt ")
                    {
                        fmtData = br.ReadBytes((int)chunkSize);
                        chunks[chunkId] = fmtData;
                    }
                    else if (chunkId == "data")
                    {
                        dataStart = fs.Position;
                        break;
                    }
                    else
                    {
                        chunks[chunkId] = br.ReadBytes((int)chunkSize);
                    }

                    long skip = chunkSize % 2 != 0 ? 1 : 0;
                    fs.Position = chunkPos + chunkSize + skip;
                }

                if (fmtData.Length == 0) { ConversionError?.Invoke(this, "文件缺少fmt chunk"); return false; }

                var format = BitConverter.ToUInt16(fmtData, 0);
                if (format != 1) { ConversionError?.Invoke(this, $"只支持PCM格式,当前类型:{format}"); return false; }

                var channels = BitConverter.ToUInt16(fmtData, 2);
                var sampleRate = BitConverter.ToUInt32(fmtData, 4);
                var blockAlign = BitConverter.ToUInt16(fmtData, 12);
                var bits = BitConverter.ToUInt16(fmtData, 14);

                ConversionProgress?.Invoke(this, $"声道数:{channels},采样率:{sampleRate},比特率:{bits}");

                using var outFs = File.Create(wavPath);
                using var bw = new BinaryWriter(outFs);

                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                long sizePos = outFs.Position;
                bw.Write((uint)0);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                foreach (var c in chunks)
                {
                    bw.Write(Encoding.ASCII.GetBytes(c.Key));
                    bw.Write((uint)c.Value.Length);
                    bw.Write(c.Value);
                    if (c.Value.Length % 2 != 0) bw.Write((byte)0);
                }

                bw.Write(Encoding.ASCII.GetBytes("data"));
                long dataSizePos = outFs.Position;
                bw.Write((uint)0);

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

                long remaining = (long)Math.Min(dataSize64, long.MaxValue);
                while (remaining > 0)
                {
                    int read = fs.Read(buffer, 0, Math.Min(buffer.Length, (int)Math.Min(remaining, int.MaxValue)));
                    if (read == 0) break;
                    bw.Write(buffer, 0, read);
                    total += read;
                    remaining -= read;
                }

                cts.Cancel();
                monitor.Wait();
                ConversionProgress?.Invoke(this, "完成复制");

                outFs.Seek(sizePos, SeekOrigin.Begin);
                bw.Write((uint)(outFs.Length - 8));
                outFs.Seek(dataSizePos, SeekOrigin.Begin);
                bw.Write((uint)total);

                var samples = (ulong)total / blockAlign;
                var duration = (double)samples / sampleRate;
                ConversionProgress?.Invoke(this, $"WAV包含{samples}个样本({blockAlign}字节每样本),时长{duration:F2}s");

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