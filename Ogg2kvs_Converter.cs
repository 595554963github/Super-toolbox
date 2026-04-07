using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Ogg2kvs_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private const string SIDECAR_MARKER = "kvs_tool_metadata";
        private const int SIDECAR_VERSION = 1;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var oggFiles = Directory.GetFiles(directoryPath, "*.ogg", SearchOption.AllDirectories)
                    .OrderBy(f =>
                    {
                        string fileName = Path.GetFileNameWithoutExtension(f);
                        var match = Regex.Match(fileName, @"_(\d+)$");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                            return num;
                        return int.MaxValue;
                    })
                    .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                    .ToArray();

                    TotalFilesToConvert = oggFiles.Length;
                    int successCount = 0;

                    if (oggFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的OGG文件");
                        OnConversionFailed("未找到需要转换的OGG文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个OGG文件");

                    foreach (var oggFilePath in oggFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(oggFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.ogg");

                        string fileDirectory = Path.GetDirectoryName(oggFilePath) ?? string.Empty;
                        string kvsFilePath = Path.Combine(fileDirectory, $"{fileName}.kvs");

                        try
                        {
                            if (EncryptFile(oggFilePath, kvsFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.kvs");
                                OnFileConverted(kvsFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.ogg转换失败");
                                OnConversionFailed($"{fileName}.ogg转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.ogg处理错误:{ex.Message}");
                        }
                    }

                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "转换操作已取消");
                OnConversionFailed("转换操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换失败:{ex.Message}");
                OnConversionFailed($"转换失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private bool LooksLikeOgg(byte[] blob)
        {
            return blob.Length >= 4 && blob[0] == 0x4F && blob[1] == 0x67 && blob[2] == 0x67 && blob[3] == 0x53;
        }

        private byte[] ReadPrefix(string path, int size)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            byte[] buffer = new byte[size];
            int read = fs.Read(buffer, 0, size);
            if (read < size)
            {
                byte[] truncated = new byte[read];
                Array.Copy(buffer, 0, truncated, 0, read);
                return truncated;
            }
            return buffer;
        }

        private string[] GetSidecarCandidates(string source)
        {
            return new string[]
            {
                $"{source}.json",
                Path.ChangeExtension(source, ".json"),
                Path.Combine(Path.GetDirectoryName(source) ?? "", $"{Path.GetFileNameWithoutExtension(source)}.ogg.json"),
                Path.Combine(Path.GetDirectoryName(source) ?? "", $"{Path.GetFileNameWithoutExtension(source)}.kvs.json"),
                Path.Combine(Path.GetDirectoryName(source) ?? "", $"{Path.GetFileNameWithoutExtension(source)}.kovs.json")
            };
        }

        private (Dictionary<string, object>? metadata, string? path) LoadSidecarFor(string source)
        {
            foreach (string candidate in GetSidecarCandidates(source))
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    string jsonText = File.ReadAllText(candidate, Encoding.UTF8);
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonText);
                    if (metadata != null && metadata.GetValueOrDefault("format")?.ToString() == SIDECAR_MARKER)
                    {
                        return (metadata, candidate);
                    }
                }
                catch
                {
                    continue;
                }
            }
            return (null, null);
        }

        private byte[] CreateHeaderBytes(uint magic, int size, int loopStart, int loopEnd, int unknown1, int unknown2, int unknown3, int unknown4)
        {
            byte[] data = new byte[Kvs2ogg_Converter.HEADER_SIZE];
            BitConverter.GetBytes(magic).CopyTo(data, 0);
            BitConverter.GetBytes(size).CopyTo(data, 4);
            BitConverter.GetBytes(loopStart).CopyTo(data, 8);
            BitConverter.GetBytes(loopEnd).CopyTo(data, 12);
            BitConverter.GetBytes(unknown1).CopyTo(data, 16);
            BitConverter.GetBytes(unknown2).CopyTo(data, 20);
            BitConverter.GetBytes(unknown3).CopyTo(data, 24);
            BitConverter.GetBytes(unknown4).CopyTo(data, 28);
            return data;
        }

        private bool EncryptFile(string sourcePath, string targetPath)
        {
            try
            {
                byte[] prefix = ReadPrefix(sourcePath, 4);
                if (!LooksLikeOgg(prefix))
                {
                    throw new InvalidDataException("不是有效的OGG文件");
                }

                int size = (int)new FileInfo(sourcePath).Length;

                var (metadata, _) = LoadSidecarFor(sourcePath);

                uint magic = 0x53564f4B;
                int loopStart = 0;
                int loopEnd = 0;
                int unknown1 = 1;
                int unknown2 = 0;
                int unknown3 = 0;
                int unknown4 = 0;
                byte[] trailer = new byte[0];

                if (metadata != null)
                {
                    if (metadata.TryGetValue("magic", out object? magicObj) && magicObj?.ToString() is string magicStr)
                    {
                        byte[] magicBytes = Encoding.ASCII.GetBytes(magicStr.PadRight(4, '\0'));
                        if (magicBytes.Length >= 4)
                        {
                            magic = BitConverter.ToUInt32(magicBytes, 0);
                        }
                    }

                    if (metadata.TryGetValue("loop_start_samples", out object? lsObj))
                        loopStart = Convert.ToInt32(lsObj);
                    if (metadata.TryGetValue("loop_end_samples", out object? leObj))
                        loopEnd = Convert.ToInt32(leObj);
                    if (metadata.TryGetValue("unknown1", out object? u1Obj))
                        unknown1 = Convert.ToInt32(u1Obj);
                    if (metadata.TryGetValue("unknown2", out object? u2Obj))
                        unknown2 = Convert.ToInt32(u2Obj);
                    if (metadata.TryGetValue("unknown3", out object? u3Obj))
                        unknown3 = Convert.ToInt32(u3Obj);
                    if (metadata.TryGetValue("unknown4", out object? u4Obj))
                        unknown4 = Convert.ToInt32(u4Obj);

                    if (metadata.TryGetValue("trailer_hex", out object? trailerObj) && trailerObj?.ToString() is string trailerHex)
                    {
                        try
                        {
                            trailer = Convert.FromHexString(trailerHex);
                        }
                        catch { }
                    }
                }

                using var writer = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

                byte[] headerBytes = CreateHeaderBytes(magic, size, loopStart, loopEnd, unknown1, unknown2, unknown3, unknown4);
                writer.Write(headerBytes, 0, Kvs2ogg_Converter.HEADER_SIZE);

                using var reader = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                Kvs2ogg_Converter.CopyXorStream(reader, writer, size);

                if (trailer.Length > 0)
                {
                    writer.Write(trailer, 0, trailer.Length);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加密失败: {ex.Message}");
                return false;
            }
        }
    }
}