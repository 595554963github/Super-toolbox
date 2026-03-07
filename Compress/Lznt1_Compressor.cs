using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Lznt1_Compressor : BaseExtractor
    {
        private const ushort COMPRESSION_FORMAT_LZNT1 = 2;
        private const ushort COMPRESSION_ENGINE_STANDARD = 0x0000;

        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint RtlGetCompressionWorkSpaceSize(
            ushort CompressionFormatAndEngine,
            out uint CompressBufferWorkSpaceSize,
            out uint CompressFragmentWorkSpaceSize
        );

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint RtlCompressBuffer(
            ushort CompressionFormatAndEngine,
            byte[] UncompressedBuffer,
            int UncompressedBufferSize,
            byte[] CompressedBuffer,
            int CompressedBufferSize,
            uint UncompressedChunkSize,
            out int FinalCompressedSize,
            IntPtr WorkSpace
        );

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(file => !file.EndsWith(".lznt1", StringComparison.OrdinalIgnoreCase))
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
                    foreach (var file in Directory.GetFiles(compressedDir, "*.lznt1", SearchOption.AllDirectories))
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
                        string outputPath = Path.Combine(compressedDir, relativePath + ".lznt1");
                        string outputDir = Path.GetDirectoryName(outputPath) ??
                            throw new InvalidOperationException($"无法确定输出目录路径:{outputPath}");

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        if (CompressLznt1File(filePath, outputPath))
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

        private bool CompressLznt1File(string inputPath, string outputPath)
        {
            try
            {
                byte[] inputData = File.ReadAllBytes(inputPath);
                byte[] compressedData = CompressLznt1(inputData);
                File.WriteAllBytes(outputPath, compressedData);
                return true;
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"压缩过程异常({Path.GetFileName(inputPath)}): {ex.Message}");
                return false;
            }
        }

        private byte[] CompressLznt1(byte[] data)
        {
            ushort format = (ushort)(COMPRESSION_FORMAT_LZNT1 | COMPRESSION_ENGINE_STANDARD);

            uint result = RtlGetCompressionWorkSpaceSize(format, out uint workSpaceSize, out _);
            if (result != 0)
                throw new Exception("获取工作空间大小失败");

            IntPtr workSpace = Marshal.AllocHGlobal((int)workSpaceSize);
            try
            {
                int compressedSize = 0;
                byte[] buffer = new byte[data.Length * 2];

                result = RtlCompressBuffer(
                    format,
                    data, data.Length,
                    buffer, buffer.Length,
                    4096,
                    out compressedSize,
                    workSpace
                );

                if (result != 0 && result != 0x00000117)
                    throw new Exception($"压缩失败,错误码:{result}");

                byte[] finalData = new byte[compressedSize];
                Array.Copy(buffer, finalData, compressedSize);
                return finalData;
            }
            finally
            {
                Marshal.FreeHGlobal(workSpace);
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