using AuroraLib.Compression.Algorithms;

namespace super_toolbox
{
    public class Yaz0_Decompressor : BaseExtractor
    {
        private static readonly byte[] Yaz0Magic = { 0x59, 0x61, 0x7A, 0x30 };
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

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsYaz0File).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到包含Yaz0数据的文件");
                        OnDecompressionFailed("未找到包含Yaz0数据的文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压,共{TotalFilesToDecompress}个文件");

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (DecompressYaz0File(filePath, decompressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, fileName);
                            string displayName = Path.GetFileName(outputPath);
                            DecompressionProgress?.Invoke(this, $"已解压:{displayName}");
                            OnFileDecompressed(outputPath);
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
                DecompressionError?.Invoke(this, $"解压失败:{ex.Message}");
                OnDecompressionFailed($"解压失败:{ex.Message}");
            }
        }

        private bool IsYaz0File(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[50];
                    int bytesRead = fs.Read(buffer, 0, 50);
                    for (int i = 0; i <= bytesRead - Yaz0Magic.Length; i++)
                    {
                        if (buffer.Skip(i).Take(Yaz0Magic.Length).SequenceEqual(Yaz0Magic))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private bool DecompressYaz0File(string inputPath, string outputDir)
        {
            try
            {
                int yaz0Start = FindYaz0StartPosition(inputPath);
                if (yaz0Start < 0)
                {
                    DecompressionError?.Invoke(this, $"未找到Yaz0数据:{Path.GetFileName(inputPath)}");
                    return false;
                }

                string fileName = Path.GetFileNameWithoutExtension(inputPath);
                string outputPath = Path.Combine(outputDir, fileName);

                using (var inputStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
                using (var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    if (yaz0Start > 0)
                    {
                        inputStream.Seek(yaz0Start, SeekOrigin.Begin);
                    }

                    Yaz0 yaz0 = new Yaz0();
                    yaz0.Decompress(inputStream, outputStream);
                }

                return true;
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"Yaz0解压错误:{ex.Message}");
                return false;
            }
        }

        private int FindYaz0StartPosition(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[50];
                int bytesRead = fs.Read(buffer, 0, 50);
                for (int i = 0; i <= bytesRead - Yaz0Magic.Length; i++)
                {
                    if (buffer.Skip(i).Take(Yaz0Magic.Length).SequenceEqual(Yaz0Magic))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
