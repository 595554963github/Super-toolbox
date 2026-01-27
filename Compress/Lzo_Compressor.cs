using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Lzo_Compressor : BaseExtractor
    {
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        static Lzo_Compressor()
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
        private const int LZO1X_999_MEM_COMPRESS = 131072 * 8;

        [DllImport("lzo2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int __lzo_init_v2(uint v, int s1, int s2, int s3, int s4, int s5, int s6, int s7, int s8, int s9);

        [DllImport("lzo2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int lzo1x_999_compress(byte[] src, int src_len, byte[] dst, ref uint dst_len, byte[] wrkmem);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            var filesToCompress = allFiles.Where(IsNotLzoFile).ToArray();

            if (filesToCompress.Length == 0)
            {
                CompressionError?.Invoke(this, "未找到需要压缩的文件");
                OnCompressionFailed("未找到需要压缩的文件");
                return;
            }

            string compressedDir = Path.Combine(directoryPath, "Compressed");
            Directory.CreateDirectory(compressedDir);
            TotalFilesToCompress = filesToCompress.Length;
            CompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(compressedDir, "*.lzo", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }

                    int result = __lzo_init_v2(2, -1, -1, -1, -1, -1, -1, -1, -1, -1);
                    if (result != LZO_E_OK)
                    {
                        CompressionError?.Invoke(this, $"LZO初始化失败:{result}");
                        OnCompressionFailed($"LZO初始化失败:{result}");
                        return;
                    }

                    int processedFiles = 0;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;
                        CompressionProgress?.Invoke(this, $"正在压缩文件({processedFiles}/{TotalFilesToCompress}): {Path.GetFileName(filePath)}");

                        string relativePath = GetRelativePath(directoryPath, filePath);
                        string outputPath = Path.Combine(compressedDir, relativePath + ".lzo");
                        string outputDir = Path.GetDirectoryName(outputPath) ??
                            throw new InvalidOperationException($"无法确定输出目录路径:{outputPath}");

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        try
                        {
                            var inputData = File.ReadAllBytes(filePath);

                            var compressedData = LZOCompress(inputData);

                            if (compressedData != null && compressedData.Length > 0)
                            {
                                File.WriteAllBytes(outputPath, compressedData);
                                CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)}");
                                OnFileCompressed(outputPath);
                            }
                            else
                            {
                                CompressionError?.Invoke(this, $"压缩失败:{filePath}");
                                OnCompressionFailed($"压缩失败:{filePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            CompressionError?.Invoke(this, $"压缩文件{filePath}时出错:{ex.Message}");
                            OnCompressionFailed($"压缩文件{filePath}时出错:{ex.Message}");
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

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private bool IsNotLzoFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension != ".lzo";
        }

        private byte[]? LZOCompress(byte[] inputData)
        {
            try
            {
                if (inputData == null || inputData.Length == 0)
                    return null;

                uint compressedSize = (uint)(inputData.Length + inputData.Length / 16 + 64 + 3);
                byte[] compressedData = new byte[compressedSize];
                byte[] workMem = new byte[LZO1X_999_MEM_COMPRESS];

                int result = lzo1x_999_compress(inputData, inputData.Length, compressedData, ref compressedSize, workMem);

                if (result != LZO_E_OK)
                    return null;

                byte[] finalData = new byte[compressedSize];
                Array.Copy(compressedData, 0, finalData, 0, (int)compressedSize);

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
