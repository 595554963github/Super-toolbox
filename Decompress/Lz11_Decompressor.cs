using AuroraLib.Compression.Algorithms;

namespace super_toolbox
{
    public class Lz11_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        private string? _sourceRootPath;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            _sourceRootPath = directoryPath;

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
                    var filesToProcess = allFiles.Where(IsLZ11File).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的LZ11压缩文件");
                        OnDecompressionFailed("未找到有效的LZ11压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压,共{TotalFilesToDecompress}个文件");

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        DecompressLZ11File(filePath, decompressedDir);
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
            finally
            {
                _sourceRootPath = null;
            }
        }

        private bool IsLZ11File(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lz11" || extension == ".lz";
        }

        private bool DecompressLZ11File(string inputPath, string outputDir)
        {
            try
            {
                if (_sourceRootPath == null)
                {
                    DecompressionError?.Invoke(this, "源文件夹路径未初始化");
                    return false;
                }

                string relativePath = GetRelativePath(_sourceRootPath, inputPath);
                string extension = Path.GetExtension(relativePath).ToLower();
                string relativePathWithoutExt = relativePath.Substring(0, relativePath.Length - extension.Length);
                string outputFilePath = Path.Combine(outputDir, relativePathWithoutExt);
                string? outputFileDir = Path.GetDirectoryName(outputFilePath);

                if (string.IsNullOrEmpty(outputFileDir))
                {
                    DecompressionError?.Invoke(this, "输出目录路径无效");
                    return false;
                }

                if (!Directory.Exists(outputFileDir))
                {
                    Directory.CreateDirectory(outputFileDir);
                }

                DecompressionProgress?.Invoke(this, $"正在解压:{Path.GetFileName(inputPath)}");

                byte[] compressedData = File.ReadAllBytes(inputPath);
                LZ11 lz11 = new LZ11();
                using (MemoryStream decompressedStream = new MemoryStream())
                using (MemoryStream compressedStream = new MemoryStream(compressedData))
                {
                    lz11.Decompress(compressedStream, decompressedStream);
                    byte[] decompressedData = decompressedStream.ToArray();
                    File.WriteAllBytes(outputFilePath, decompressedData);
                }

                if (File.Exists(outputFilePath))
                {
                    OnFileDecompressed(outputFilePath);
                    return true;
                }
                else
                {
                    DecompressionError?.Invoke(this, $"解压成功但输出文件不存在:{outputFilePath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"LZ11解压错误:{ex.Message}");
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