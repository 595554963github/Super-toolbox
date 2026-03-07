using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Mszip_Compressor : BaseExtractor
    {
        private const uint COMPRESS_ALGORITHM_MSZIP = 2;

        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CreateCompressor(uint algorithm, IntPtr allocationRoutines, out IntPtr compressorHandle);

        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool Compress(IntPtr compressorHandle, byte[] inputData, int inputSize, byte[] outputBuffer, int outputBufferSize, out int compressedSize);

        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CloseCompressor(IntPtr compressorHandle);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(file => !file.EndsWith(".mszip", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (filesToCompress.Length == 0)
            {
                CompressionError?.Invoke(this, "未找到需要压缩的文件");
                OnCompressionFailed("未找到需要压缩的文件");
                return;
            }

            string compressedDir = Path.Combine(directoryPath, "Compressed");
            Directory.CreateDirectory(compressedDir);

            CompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(compressedDir, "*.mszip", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToCompress = filesToCompress.Length;
                    int processedFiles = 0;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;

                        CompressionProgress?.Invoke(this, $"正在压缩文件({processedFiles}/{TotalFilesToCompress}): {Path.GetFileName(filePath)}");

                        string relativePath = GetRelativePath(directoryPath, filePath);
                        string outputPath = Path.Combine(compressedDir, relativePath + ".mszip");
                        string outputDir = Path.GetDirectoryName(outputPath) ??
                            throw new InvalidOperationException($"无法确定输出目录路径:{outputPath}");

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        if (CompressMSZipFile(filePath, outputPath))
                        {
                            CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)}");
                            OnFileCompressed(outputPath);
                        }
                        else
                        {
                            CompressionError?.Invoke(this, $"压缩失败:{Path.GetFileName(filePath)}");
                            OnCompressionFailed($"压缩失败:{Path.GetFileName(filePath)}");
                        }
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
                CompressionError?.Invoke(this, $"压缩过程出错:{ex.Message}");
                OnCompressionFailed($"压缩过程出错:{ex.Message}");
            }
        }

        private bool CompressMSZipFile(string inputPath, string outputPath)
        {
            try
            {
                byte[] inputData = File.ReadAllBytes(inputPath);
                byte[] compressedData = CompressMSZip(inputData);
                File.WriteAllBytes(outputPath, compressedData);
                return true;
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"压缩过程异常({Path.GetFileName(inputPath)}): {ex.Message}");
                return false;
            }
        }

        private byte[] CompressMSZip(byte[] data)
        {
            IntPtr compressor = IntPtr.Zero;
            try
            {
                if (!CreateCompressor(COMPRESS_ALGORITHM_MSZIP, IntPtr.Zero, out compressor))
                    throw new Exception("创建压缩器失败");

                int compressedSize = 0;
                Compress(compressor, data, data.Length, Array.Empty<byte>(), 0, out compressedSize);

                byte[] buffer = new byte[compressedSize];
                if (!Compress(compressor, data, data.Length, buffer, buffer.Length, out compressedSize))
                    throw new Exception("压缩失败");

                Array.Resize(ref buffer, compressedSize);
                return buffer;
            }
            finally
            {
                if (compressor != IntPtr.Zero)
                    CloseCompressor(compressor);
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