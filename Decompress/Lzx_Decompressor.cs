using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Lzx_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        static Lzx_Decompressor()
        {
            LoadLzxDll();
        }

        private static void LoadLzxDll()
        {
            LoadEmbeddedDll("embedded.libLZX.dll", "libLZX.dll");
        }

        [DllImport("libLZX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int CalcDecompressedSize(byte[] src, uint src_size, out uint decompressed_size);

        [DllImport("libLZX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Decompress(byte[] src, uint src_size, byte[] dest, out uint decompressed_size);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            var filesToProcess = allFiles.Where(IsLzxFile).ToArray();

            if (filesToProcess.Length == 0)
            {
                DecompressionError?.Invoke(this, "未找到有效的LZX压缩文件");
                OnDecompressionFailed("未找到有效的LZX压缩文件");
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
                            var decompressedData = DecompressLzx(compressedData);

                            if (decompressedData != null && decompressedData.Length > 0)
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

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private bool IsLzxFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lzx";
        }

        private byte[]? DecompressLzx(byte[] compressedData)
        {
            try
            {
                if (compressedData == null || compressedData.Length == 0)
                    return null;

                int sizeResult = CalcDecompressedSize(compressedData, (uint)compressedData.Length, out uint decompressedSize);
                if (sizeResult != 0 || decompressedSize == 0)
                    return null;

                byte[] outputData = new byte[decompressedSize];
                int decompressResult = Decompress(compressedData, (uint)compressedData.Length, outputData, out uint actualSize);

                if (decompressResult == 0 && actualSize == decompressedSize && actualSize > 0)
                {
                    return outputData;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
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
    }
}