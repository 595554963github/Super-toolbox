using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Lzms_Decompressor : BaseExtractor
    {
        private const uint COMPRESS_ALGORITHM_LZMS = 5;

        public event EventHandler<string>? DecompressionStarted;
        public event EventHandler<string>? DecompressionProgress;
        public event EventHandler<string>? DecompressionError;

        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CreateDecompressor(uint algorithm, IntPtr allocationRoutines, out IntPtr decompressorHandle);

        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool Decompress(IntPtr decompressorHandle, byte[] compressedData, int compressedSize, byte[] outputBuffer, int outputBufferSize, out int decompressedSize);

        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CloseDecompressor(IntPtr decompressorHandle);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            var filesToProcess = allFiles.Where(f => f.EndsWith(".lzms", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (filesToProcess.Length == 0)
            {
                DecompressionError?.Invoke(this, "未找到有效的LZMS压缩文件");
                OnDecompressionFailed("未找到有效的LZMS压缩文件");
                return;
            }

            string decompressedDir = Path.Combine(directoryPath, "Decompressed");
            Directory.CreateDirectory(decompressedDir);
            TotalFilesToDecompress = filesToProcess.Length;
            DecompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    int processedFiles = 0;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;

                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        string relativePath = GetRelativePath(directoryPath, filePath);
                        string outputPath = Path.Combine(decompressedDir, Path.ChangeExtension(relativePath, null));
                        string? outputDir = Path.GetDirectoryName(outputPath);

                        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        try
                        {
                            var compressedData = File.ReadAllBytes(filePath);
                            var decompressedData = DecompressLzms(compressedData);

                            if (decompressedData.Length > 0)
                            {
                                File.WriteAllBytes(outputPath, decompressedData);
                                DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)}");
                                OnFileDecompressed(outputPath);
                            }
                            else
                            {
                                DecompressionError?.Invoke(this, $"解压失败:{filePath}");
                                OnDecompressionFailed($"解压失败:{filePath}");
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

        private byte[] DecompressLzms(byte[] compressedData)
        {
            IntPtr decompressor = IntPtr.Zero;
            try
            {
                if (!CreateDecompressor(COMPRESS_ALGORITHM_LZMS, IntPtr.Zero, out decompressor))
                    throw new Exception("创建解压器失败");

                int decompressedSize = 0;
                Decompress(decompressor, compressedData, compressedData.Length, Array.Empty<byte>(), 0, out decompressedSize);

                byte[] buffer = new byte[decompressedSize];
                if (!Decompress(decompressor, compressedData, compressedData.Length, buffer, buffer.Length, out decompressedSize))
                    throw new Exception("解压失败");

                Array.Resize(ref buffer, decompressedSize);
                return buffer;
            }
            finally
            {
                if (decompressor != IntPtr.Zero)
                    CloseDecompressor(decompressor);
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