using csharp_prs;

namespace super_toolbox
{
    public class PRS_Decompressor : BaseExtractor
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
                    var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
                    var filesToProcess = new List<string>();

                    foreach (var filePath in allFiles)
                    {
                        if (IsPRSCompressedFile(filePath))
                        {
                            filesToProcess.Add(filePath);
                        }
                    }

                    if (filesToProcess.Count == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到PRS压缩文件");
                        OnDecompressionFailed("未找到PRS压缩文件");
                        return;
                    }

                    TotalFilesToDecompress = filesToProcess.Count;
                    int processedFiles = 0;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;

                        DecompressionProgress?.Invoke(this,
                            $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        string relativePath = GetRelativePath(directoryPath, filePath);
                        string outputPath = Path.Combine(decompressedDir, relativePath);
                        string? outputDir = Path.GetDirectoryName(outputPath);

                        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        try
                        {
                            byte[] compressedData = File.ReadAllBytes(filePath);
                            byte[] decompressedData = Prs.Decompress(compressedData);
                            File.WriteAllBytes(outputPath, decompressedData);

                            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                            {
                                DecompressionProgress?.Invoke(this,
                                    $"已解压:{Path.GetFileName(outputPath)} " +
                                    $"(压缩后:{compressedData.Length}字节, 解压后:{decompressedData.Length}字节)");
                                OnFileDecompressed(outputPath);
                            }
                            else
                            {
                                DecompressionError?.Invoke(this, $"解压成功但输出文件异常:{outputPath}");
                                OnDecompressionFailed($"解压成功但输出文件异常:{outputPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DecompressionError?.Invoke(this, $"解压失败({filePath}): {ex.Message}");
                            OnDecompressionFailed($"解压失败({filePath}): {ex.Message}");
                        }
                    }

                    OnDecompressionCompleted();
                    DecompressionProgress?.Invoke(this, $"解压完成，共解压{TotalFilesToDecompress}个文件");
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

        private bool IsPRSCompressedFile(string filePath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);

                if (data.Length < 4)
                    return false;

                try
                {
                    Prs.Estimate(data);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}