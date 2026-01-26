using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class LzhamStandard_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        static LzhamStandard_Decompressor()
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
                    throw new FileNotFoundException($"嵌入的LZHAM资源未找到:{resourceName}");

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                File.WriteAllBytes(outputPath, buffer);
            }
        }

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
                    var filesToProcess = allFiles.Where(IsLzhamFile).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的LZHAM压缩文件");
                        OnDecompressionFailed("未找到有效的LZHAM压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压,共{TotalFilesToDecompress}个文件");

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DecompressLzhamFile(filePath, decompressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, fileName);
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

        private bool IsLzhamFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lzham";
        }

        private bool DecompressLzhamFile(string inputPath, string outputDir)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(inputPath);
                string outputPath = Path.Combine(outputDir, fileName);

                DecompressionProgress?.Invoke(this, $"正在解压:{Path.GetFileName(inputPath)}");

                byte[] compressedData = File.ReadAllBytes(inputPath);

                var parameters = new global::LzhamWrapper.DecompressionParameters
                {
                    DictionarySize = 26,
                    UpdateRate = global::LzhamWrapper.Enums.TableUpdateRate.Default,
                    Flags = global::LzhamWrapper.Enums.DecompressionFlag.ComputeAdler32
                };

                uint adler32 = 0;
                int outBufSize = compressedData.Length * 10;
                byte[] outBuf = new byte[outBufSize];

                var status = global::LzhamWrapper.Lzham.DecompressMemory(parameters, compressedData, compressedData.Length, 0,
                    outBuf, ref outBufSize, 0, ref adler32);

                if (status != global::LzhamWrapper.Enums.DecompressStatus.Success)
                {
                    DecompressionError?.Invoke(this, $"LZHAM解压失败: {status}");
                    return false;
                }

                byte[] decompressedData = new byte[outBufSize];
                Array.Copy(outBuf, 0, decompressedData, 0, outBufSize);

                File.WriteAllBytes(outputPath, decompressedData);

                if (File.Exists(outputPath))
                {
                    DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)}");
                    return true;
                }
                else
                {
                    DecompressionError?.Invoke(this, $"解压成功但输出文件不存在:{outputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"LZHAM解压错误:{ex.Message}");
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
