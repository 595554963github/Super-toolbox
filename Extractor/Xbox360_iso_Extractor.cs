using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Xbox360_iso_Extractor : BaseExtractor
    {
        private static string _tempDllPath;
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        [DllImport("exiso.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int extract_xiso_extract(string xisoPath, string outputDir);

        [DllImport("exiso.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int extract_xiso_set_quiet(int quiet);

        static Xbox360_iso_Extractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempDllPath = Path.Combine(tempDir, "exiso.dll");
            ExtractEmbeddedResource("embedded.exiso.dll", _tempDllPath);
        }

        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            if (!File.Exists(outputPath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException($"嵌入的exiso资源未找到:{resourceName}");
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(outputPath, buffer);
                }
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:选择的目录不存在");
                OnExtractionFailed("错误:选择的目录不存在");
                return;
            }

            var isoFiles = Directory.EnumerateFiles(directoryPath, "*.iso", SearchOption.AllDirectories).ToList();
            if (isoFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.iso文件");
                OnExtractionFailed("未找到任何.iso文件");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            ExtractionProgress?.Invoke(this, $"找到{isoFiles.Count}个.iso文件,开始提取...");

            try
            {
                await Task.Run(() =>
                {
                    int totalFilesExtracted = 0;
                    int totalIsoProcessed = 0;

                    foreach (var isoFilePath in isoFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        totalIsoProcessed++;

                        string fileName = Path.GetFileName(isoFilePath);
                        string isoFileNameWithoutExt = Path.GetFileNameWithoutExtension(isoFilePath);
                        string extractDir = Path.Combine(directoryPath, isoFileNameWithoutExt);

                        Directory.CreateDirectory(extractDir);

                        ExtractionProgress?.Invoke(this, $"正在提取({totalIsoProcessed}/{isoFiles.Count}):{fileName}");

                        try
                        {
                            extract_xiso_set_quiet(1);
                            int result = extract_xiso_extract(isoFilePath, extractDir);

                            if (result != 0)
                            {
                                ExtractionError?.Invoke(this, $"{fileName}提取失败,错误代码:{result}");
                                OnExtractionFailed($"{fileName}提取失败,错误代码:{result}");
                            }
                            else
                            {
                                if (Directory.Exists(extractDir))
                                {
                                    var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                                    int fileCount = extractedFiles.Length;
                                    totalFilesExtracted += fileCount;
                                    foreach (var extractedFile in extractedFiles)
                                    {
                                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(extractedFile)}");
                                        OnFileExtracted(extractedFile);
                                    }
                                }

                                ExtractionProgress?.Invoke(this, $"提取成功:{fileName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"提取异常:{ex.Message}");
                            OnExtractionFailed($"{fileName}处理错误:{ex.Message}");
                        }
                    }

                    TotalFilesToExtract = totalFilesExtracted;
                    ExtractionProgress?.Invoke(this, $"处理完成,共提取{totalFilesExtracted}个文件");
                    OnExtractionCompleted();

                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}