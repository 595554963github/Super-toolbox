namespace super_toolbox
{
    public class Lzma_Decompressor : BaseExtractor
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
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(f => Path.GetExtension(f).ToLower() == ".lzma").ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的LZMA压缩文件");
                        OnDecompressionFailed("未找到有效的LZMA压缩文件");
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
                            if (DecompressLzmaFile(filePath, outputPath))
                            {
                                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                                {
                                    DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)}");
                                    OnFileDecompressed(outputPath);
                                }
                                else
                                {
                                    DecompressionError?.Invoke(this, $"解压成功但输出文件异常:{outputPath}");
                                    OnDecompressionFailed($"解压成功但输出文件异常:{outputPath}");
                                }
                            }
                            else
                            {
                                DecompressionError?.Invoke(this, $"解压文件失败:{filePath}");
                                OnDecompressionFailed($"解压文件失败:{filePath}");
                            }
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

        private bool DecompressLzmaFile(string inputPath, string outputPath)
        {
            try
            {
                byte[] compressedData = File.ReadAllBytes(inputPath);
                byte[] decompressedData = LzmaHelper.Decompress(compressedData);
                File.WriteAllBytes(outputPath, decompressedData);
                return true;
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
