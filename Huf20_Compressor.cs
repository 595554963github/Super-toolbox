using AuroraLib.Compression.Algorithms;

namespace super_toolbox
{
    public class Huf20_Compressor : BaseExtractor
    {
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;
        private string? _sourceRootPath;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            _sourceRootPath = directoryPath;

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
                    var filesToProcess = allFiles.Where(IsNotHuf20File).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        CompressionError?.Invoke(this, "未找到需要压缩的文件");
                        OnCompressionFailed("未找到需要压缩的文件");
                        return;
                    }

                    string compressedDir = Path.Combine(directoryPath, "Compressed");
                    Directory.CreateDirectory(compressedDir);
                    TotalFilesToCompress = filesToProcess.Length;
                    CompressionStarted?.Invoke(this, $"开始压缩,共{TotalFilesToCompress}个文件");

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CompressHUF20File(filePath, compressedDir);
                    }

                    OnCompressionCompleted();
                    CompressionProgress?.Invoke(this, $"压缩完成,共压缩{TotalFilesToCompress}个文件");
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CompressionError?.Invoke(this, "压缩操作已取消");
                OnCompressionFailed("压缩操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"压缩失败:{ex.Message}");
                OnCompressionFailed($"压缩失败:{ex.Message}");
            }
            finally
            {
                _sourceRootPath = null;
            }
        }

        private bool IsNotHuf20File(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension != ".huf";
        }

        private bool CompressHUF20File(string inputPath, string outputDir)
        {
            try
            {
                if (_sourceRootPath == null)
                {
                    CompressionError?.Invoke(this, "源文件夹路径未初始化");
                    return false;
                }

                string relativePath = GetRelativePath(_sourceRootPath, inputPath);
                string outputFilePath = Path.Combine(outputDir, relativePath + ".huf");
                string? outputFileDir = Path.GetDirectoryName(outputFilePath);

                if (string.IsNullOrEmpty(outputFileDir))
                {
                    CompressionError?.Invoke(this, "输出目录路径无效");
                    return false;
                }

                if (!Directory.Exists(outputFileDir))
                {
                    Directory.CreateDirectory(outputFileDir);
                }

                CompressionProgress?.Invoke(this, $"正在压缩:{Path.GetFileName(inputPath)}");

                byte[] originalData = File.ReadAllBytes(inputPath);
                HUF20 huf20 = new HUF20();
                using (MemoryStream compressedStream = new MemoryStream())
                {
                    huf20.Compress(originalData, compressedStream);
                    byte[] compressedData = compressedStream.ToArray();
                    File.WriteAllBytes(outputFilePath, compressedData);
                }

                if (File.Exists(outputFilePath))
                {
                    OnFileCompressed(outputFilePath);
                    return true;
                }
                else
                {
                    CompressionError?.Invoke(this, $"压缩成功但输出文件不存在:{outputFilePath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"huf20压缩错误:{ex.Message}");
                return false;
            }
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(fullPath))
            {
                return string.Empty;
            }

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