using ZstdNet;

namespace super_toolbox
{
    public class TOTK_zs_Extractor : BaseExtractor
    {
        private static readonly byte[] ZstdMagic = { 0x28, 0xB5, 0x2F, 0xFD };
        private static string _zsDicPath;
        private static string _packDicPath;
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        static TOTK_zs_Extractor()
        {
            _zsDicPath = LoadEmbeddedFile("embedded.zs.zsdic", "zs.zsdic");
            _packDicPath = LoadEmbeddedFile("embedded.pack.zsdic", "pack.zsdic");
        }
        private static string LoadEmbeddedFile(string resourceName, string fileName)
        {
            string filePath = Path.Combine(TempDllDirectory, fileName);
            if (!File.Exists(filePath))
            {
                using var stream = typeof(TOTK_zs_Extractor).Assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    throw new FileNotFoundException($"嵌入的资源'{resourceName}'未找到");
                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                File.WriteAllBytes(filePath, buffer);
            }
            return filePath;
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.zs", SearchOption.AllDirectories);
                    var filesToProcess = allFiles.Where(IsZstdFile).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        ExtractionError?.Invoke(this, "未找到有效的.zs压缩文件");
                        OnExtractionFailed("未找到有效的.zs压缩文件");
                        return;
                    }

                    TotalFilesToExtract = filesToProcess.Length;
                    int processedFiles = 0;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;
                        ExtractionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToExtract}): {Path.GetFileName(filePath)}");

                        string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                        string fileName = Path.GetFileName(filePath);
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                        string extractedDir = Path.Combine(fileDirectory, fileNameWithoutExt);
                        Directory.CreateDirectory(extractedDir);
                        string outputPath = Path.Combine(extractedDir, fileNameWithoutExt);

                        try
                        {
                            byte[] dict = fileName.Contains(".pack.zs") ? File.ReadAllBytes(_packDicPath) : File.ReadAllBytes(_zsDicPath);
                            var options = new DecompressionOptions(dict);

                            if (DecompressZstdFile(filePath, outputPath, options))
                            {
                                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                                {
                                    ExtractionProgress?.Invoke(this, $"已解压:{fileNameWithoutExt}");
                                    OnFileExtracted(outputPath);
                                }
                                else
                                {
                                    ExtractionError?.Invoke(this, $"解压成功但输出文件异常:{outputPath}");
                                    OnExtractionFailed($"解压成功但输出文件异常:{outputPath}");
                                }
                            }
                            else
                            {
                                ExtractionError?.Invoke(this, $"解压文件失败:{filePath}");
                                OnExtractionFailed($"解压文件失败:{filePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"解压文件{filePath}时出错:{ex.Message}");
                            OnExtractionFailed($"解压文件{filePath}时出错:{ex.Message}");
                        }
                    }

                    OnExtractionCompleted();
                    ExtractionProgress?.Invoke(this, $"解压完成,共解压{TotalFilesToExtract}个文件");
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "解压操作已取消");
                OnExtractionFailed("解压操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解压过程出错:{ex.Message}");
                OnExtractionFailed($"解压过程出错:{ex.Message}");
            }
        }

        private bool IsZstdFile(string filePath)
        {
            try
            {
                using var file = File.OpenRead(filePath);
                if (file.Length < 4) return false;
                byte[] header = new byte[4];
                file.Read(header, 0, 4);
                return header.SequenceEqual(ZstdMagic);
            }
            catch
            {
                return false;
            }
        }

        private bool DecompressZstdFile(string inputPath, string outputPath, DecompressionOptions options)
        {
            try
            {
                using var decompressor = new Decompressor(options);
                using var inputStream = File.OpenRead(inputPath);
                using var outputStream = File.Create(outputPath);
                byte[] compressedData = new byte[inputStream.Length];
                inputStream.Read(compressedData, 0, compressedData.Length);
                byte[] decompressedData = decompressor.Unwrap(compressedData);
                outputStream.Write(decompressedData, 0, decompressedData.Length);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}