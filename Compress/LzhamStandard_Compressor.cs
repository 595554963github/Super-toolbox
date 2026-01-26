using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class LzhamStandard_Compressor : BaseExtractor
    {
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        static LzhamStandard_Compressor()
        {
            LoadLzhamDll();
        }

        private static void LoadLzhamDll()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_lzham");
            Directory.CreateDirectory(tempDir);
            string dllPath = Path.Combine(tempDir, "lzham_x64.dll");

            if (!File.Exists(dllPath))
            {
                ExtractEmbeddedResource("embedded.lzham_x64.dll", dllPath);
            }

            NativeLibrary.Load(dllPath);
        }

        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException($"嵌入的LZHAM资源未找到: {resourceName}");

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                File.WriteAllBytes(outputPath, buffer);
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsNotLzhamFile).ToArray();
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
                        if (CompressLzhamFile(filePath, compressedDir))
                        {
                            string fileName = Path.GetFileName(filePath);
                            string outputPath = Path.Combine(compressedDir, fileName + ".lzham");
                            OnFileCompressed(outputPath);
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
                CompressionError?.Invoke(this, $"压缩失败:{ex.Message}");
                OnCompressionFailed($"压缩失败:{ex.Message}");
            }
        }

        private bool IsNotLzhamFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension != ".lzham";
        }

        private bool CompressLzhamFile(string inputPath, string outputDir)
        {
            try
            {
                string fileName = Path.GetFileName(inputPath);
                string outputPath = Path.Combine(outputDir, fileName + ".lzham");

                CompressionProgress?.Invoke(this, $"正在压缩:{fileName}");

                byte[] originalData = File.ReadAllBytes(inputPath);

                var parameters = new global::LzhamWrapper.CompressionParameters
                {
                    DictionarySize = 26,
                    Level = global::LzhamWrapper.Enums.CompressionLevel.Default,
                    UpdateRate = global::LzhamWrapper.Enums.TableUpdateRate.Default,
                    HelperThreads = 0,
                    Flags = global::LzhamWrapper.Enums.CompressionFlag.DeterministicParsing
                };

                uint adler32 = 0;
                int outBufSize = originalData.Length + 1024;
                byte[] outBuf = new byte[outBufSize];

                var status = global::LzhamWrapper.Lzham.CompressMemory(parameters, originalData, originalData.Length, 0,
                    outBuf, ref outBufSize, 0, ref adler32);

                if (status != global::LzhamWrapper.Enums.CompressStatus.Success)
                {
                    CompressionError?.Invoke(this, $"LZHAM压缩失败:{status}");
                    return false;
                }

                byte[] compressedData = new byte[outBufSize];
                Array.Copy(outBuf, 0, compressedData, 0, outBufSize);

                File.WriteAllBytes(outputPath, compressedData);

                if (File.Exists(outputPath))
                {
                    CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)}");
                    return true;
                }
                else
                {
                    CompressionError?.Invoke(this, $"压缩成功但输出文件不存在:{outputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"LZHAM压缩错误:{ex.Message}");
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
