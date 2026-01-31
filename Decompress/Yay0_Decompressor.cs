using AuroraLib.Compression.Algorithms;

namespace super_toolbox
{
    public class Yay0_Decompressor : BaseExtractor
    {      
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        private static readonly byte[] Yay0Magic = { 0x59, 0x61, 0x79, 0x30 };
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
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsYay0File).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的Yay0压缩文件");
                        OnDecompressionFailed("未找到有效的Yay0压缩文件");
                        return;
                    }

                    TotalFilesToDecompress = filesToProcess.Length;
                    int processedFiles = 0;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;

                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(decompressedDir, fileName);

                        try
                        {
                            using (var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                            using (var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                            {
                                Yay0 yay0 = new Yay0();
                                yay0.Decompress(inputStream, outputStream);
                            }

                            FileInfo outputInfo = new FileInfo(outputPath);
                            DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)} (大小: {outputInfo.Length} 字节)");
                            OnFileDecompressed(outputPath);
                        }
                        catch (Exception ex)
                        {
                            DecompressionError?.Invoke(this, $"解压文件{filePath}时出错:{ex.Message}");
                            OnDecompressionFailed($"解压文件{filePath}时出错:{ex.Message}");
                        }
                    }

                    OnDecompressionCompleted();
                    DecompressionProgress?.Invoke(this, $"解压完成,共解压{TotalFilesToDecompress}个文件");
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

        private bool IsYay0File(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 16) return false;
                    byte[] header = new byte[4];
                    int bytesRead = fs.Read(header, 0, 4);
                    return bytesRead == 4 && header.SequenceEqual(Yay0Magic);
                }
            }
            catch
            {
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
