using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Lzo_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        static Lzo_Decompressor()
        {
            LoadLzoDll();
        }

        private static void LoadLzoDll()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            string dllPath = Path.Combine(tempDir, "lzo2.dll");

            if (!File.Exists(dllPath))
            {
                ExtractEmbeddedResource("embedded.lzo2.dll", dllPath);
            }

            NativeLibrary.Load(dllPath);
        }

        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException($"嵌入的LZO资源未找到:{resourceName}");

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                File.WriteAllBytes(outputPath, buffer);
            }
        }

        private const int LZO_E_OK = 0;

        [DllImport("lzo2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int __lzo_init_v2(uint v, int s1, int s2, int s3, int s4, int s5, int s6, int s7, int s8, int s9);

        [DllImport("lzo2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lzo1x_decompress(byte[] src, int src_len, byte[] dst, ref uint dst_len, IntPtr wrkmem);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            var filesToProcess = allFiles.Where(IsLzoFile).ToArray();

            if (filesToProcess.Length == 0)
            {
                DecompressionError?.Invoke(this, "未找到有效的LZO压缩文件");
                OnDecompressionFailed("未找到有效的LZO压缩文件");
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
                    int result = __lzo_init_v2(2, -1, -1, -1, -1, -1, -1, -1, -1, -1);
                    if (result != LZO_E_OK)
                    {
                        DecompressionError?.Invoke(this, $"LZO初始化失败:{result}");
                        OnDecompressionFailed($"LZO初始化失败:{result}");
                        return;
                    }

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

                            var decompressedData = LZODecompress(compressedData);

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

        private bool IsLzoFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lzo";
        }

        private byte[]? LZODecompress(byte[] compressedData)
        {
            try
            {
                if (compressedData == null || compressedData.Length == 0)
                    return null;

                uint decompressedSize = (uint)(compressedData.Length * 10);
                byte[] decompressedData = new byte[decompressedSize];

                int result = lzo1x_decompress(compressedData, compressedData.Length, decompressedData, ref decompressedSize, IntPtr.Zero);

                if (result != LZO_E_OK)
                    return null;

                byte[] finalData = new byte[decompressedSize];
                Array.Copy(decompressedData, 0, finalData, 0, (int)decompressedSize);

                return finalData;
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
