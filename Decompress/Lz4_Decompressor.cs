using K4os.Compression.LZ4.Streams;

namespace super_toolbox
{
    public class Lz4_Decompressor : BaseExtractor
    {
        private static readonly byte[] LZ4MagicNumber = new byte[] { 0x04, 0x22, 0x4D, 0x18 };
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
                    var filesToProcess = allFiles.Where(IsLz4File).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的LZ4压缩文件");
                        OnDecompressionFailed("未找到有效的LZ4压缩文件");
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
                            if (DecompressLz4File(filePath, outputPath))
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
        private bool IsLz4File(string filePath)
        {
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    if (file.Length < 4) return false;

                    byte[] header = new byte[4];
                    file.Read(header, 0, 4);

                    return header.SequenceEqual(LZ4MagicNumber);
                }
            }
            catch { }
            return false;
        }
        private bool DecompressLz4File(string inputPath, string outputPath)
        {
            try
            {
                using (var inputStream = File.OpenRead(inputPath))
                using (var outputStream = File.Create(outputPath))
                using (var decompressionStream = LZ4Stream.Decode(inputStream))
                {
                    decompressionStream.CopyTo(outputStream);
                }
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