using AuroraLib.Compression.Algorithms;

namespace super_toolbox
{
    public class Mio0Standard_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            string decompressedDir = Path.Combine(directoryPath, "Decompressed");
            Directory.CreateDirectory(decompressedDir);

            DecompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.mio0", SearchOption.AllDirectories);

                    if (allFiles.Length == 0)
                    {
                        var filesWithMio0 = new List<string>();
                        foreach (var filePath in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
                        {
                            if (ContainsMio0Data(filePath))
                            {
                                filesWithMio0.Add(filePath);
                            }
                        }
                        allFiles = filesWithMio0.ToArray();
                    }

                    if (allFiles.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的MIO0压缩文件");
                        OnDecompressionFailed("未找到有效的MIO0压缩文件");
                        return;
                    }

                    TotalFilesToDecompress = allFiles.Length;
                    int processedFiles = 0;

                    foreach (var filePath in allFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedFiles++;

                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        try
                        {
                            var results = DecompressAllMio0InFile(filePath, decompressedDir);
                            foreach (var result in results)
                            {
                                if (result.success)
                                {
                                    DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(result.outputPath)}");
                                    OnFileDecompressed(result.outputPath);
                                }
                                else
                                {
                                    DecompressionError?.Invoke(this, $"解压失败:{result.error}");
                                    OnDecompressionFailed($"解压失败:{result.error}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DecompressionError?.Invoke(this, $"解压文件{filePath}时出错:{ex.Message}");
                            OnDecompressionFailed($"解压文件{filePath}时出错:{ex.Message}");
                        }
                    }

                    OnDecompressionCompleted();
                    DecompressionProgress?.Invoke(this, $"解压完成,共处理{TotalFilesToDecompress}个文件");
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                DecompressionError?.Invoke(this, "解压操作已取消");
                OnDecompressionFailed("解压操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"解压过程出错:{ex.Message}");
                OnDecompressionFailed($"解压过程出错:{ex.Message}");
            }
        }

        private bool ContainsMio0Data(string filePath)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                return FindMio0Indices(fileData).Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private List<int> FindMio0Indices(byte[] data)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i <= data.Length - 4; i++)
            {
                if (data[i] == 'M' && data[i + 1] == 'I' && data[i + 2] == 'O' && data[i + 3] == '0')
                {
                    indices.Add(i);
                }
            }
            return indices;
        }

        private List<(bool success, string outputPath, string error)> DecompressAllMio0InFile(string filePath, string outputDir)
        {
            var results = new List<(bool, string, string)>();
            byte[] fileData = File.ReadAllBytes(filePath);

            var mio0Indices = FindMio0Indices(fileData);

            if (mio0Indices.Count == 0 && IsMio0File(filePath))
            {
                mio0Indices.Add(0);
            }

            for (int i = 0; i < mio0Indices.Count; i++)
            {
                int offset = mio0Indices[i];
                try
                {
                    byte[] decompressedData = DecompressMio0(fileData, offset);

                    string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                    string outputFileName = mio0Indices.Count > 1 ? $"{baseFileName}_{i}" : baseFileName;
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    File.WriteAllBytes(outputPath, decompressedData);

                    results.Add((true, outputPath, ""));
                }
                catch (Exception ex)
                {
                    results.Add((false, "", $"偏移量0x{offset:X8}处解压失败: {ex.Message}"));
                }
            }

            return results;
        }

        private bool IsMio0File(string filePath)
        {
            try
            {
                using var file = File.OpenRead(filePath);
                if (file.Length < 4) return false;
                byte[] header = new byte[4];
                file.Read(header, 0, 4);
                return System.Text.Encoding.ASCII.GetString(header) == "MIO0";
            }
            catch
            {
                return false;
            }
        }

        private byte[] DecompressMio0(byte[] data, int offset)
        {
            using MemoryStream inputStream = new MemoryStream(data, offset, data.Length - offset);
            using MemoryStream outputStream = new MemoryStream();

            MIO0 decompressor = new MIO0();
            decompressor.Decompress(inputStream, outputStream);

            return outputStream.ToArray();
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}