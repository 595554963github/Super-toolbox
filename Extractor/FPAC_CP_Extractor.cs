using System.Diagnostics;

namespace super_toolbox
{
    public class FPAC_CP_Extractor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        static FPAC_CP_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.exah3pac.exe", "exah3pac.exe");
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:选择的目录不存在");
                OnExtractionFailed("错误:选择的目录不存在");
                return;
            }
            var pacFiles = Directory.GetFiles(directoryPath, "*.pac", SearchOption.AllDirectories);
            if (pacFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.pac文件");
                OnExtractionFailed("未找到任何.pac文件");
                return;
            }
            TotalFilesToExtract = pacFiles.Length;
            ExtractionStarted?.Invoke(this, $"找到{pacFiles.Length}个.pac文件，开始解包...");

            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    int totalExtractedFiles = 0;
                    var allExistingFilesBefore = GetAllNonPacFiles(directoryPath);
                    foreach (var pacFilePath in pacFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string fileDirectory = Path.GetDirectoryName(pacFilePath) ?? string.Empty;
                        string fileName = Path.GetFileName(pacFilePath);
                        ExtractionProgress?.Invoke(this, $"正在解包:{fileName}");
                        try
                        {
                            var filesBefore = GetAllNonPacFiles(fileDirectory);
                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"\"{pacFilePath}\"",
                                WorkingDirectory = fileDirectory,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };
                            using (var process = Process.Start(processStartInfo))
                            {
                                if (process == null)
                                {
                                    ExtractionError?.Invoke(this, $"无法启动解包进程:{fileName}");
                                    OnExtractionFailed($"无法启动解包进程:{fileName}");
                                    continue;
                                }
                                process.OutputDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        ExtractionProgress?.Invoke(this, e.Data);
                                    }
                                };
                                process.ErrorDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        ExtractionError?.Invoke(this, $"错误:{e.Data}");
                                    }
                                };
                                process.BeginOutputReadLine();
                                process.BeginErrorReadLine();
                                process.WaitForExit();
                                if (process.ExitCode != 0)
                                {
                                    ExtractionError?.Invoke(this, $"{fileName}解包失败，错误代码:{process.ExitCode}");
                                    OnExtractionFailed($"{fileName}解包失败，错误代码:{process.ExitCode}");
                                }
                                else
                                {
                                    ExtractionProgress?.Invoke(this, $"解包成功:{fileName}");
                                    processedCount++;
                                    Thread.Sleep(100);
                                    var filesAfter = GetAllNonPacFiles(fileDirectory);
                                    var newFiles = filesAfter.Except(filesBefore).ToList();
                                    foreach (var extractedFile in newFiles)
                                    {
                                        string extractedFileName = Path.GetFileName(extractedFile);
                                        OnFileExtracted(extractedFile);
                                    }
                                    totalExtractedFiles += newFiles.Count;
                                    ExtractionProgress?.Invoke(this, $"本次提取出{newFiles.Count}个文件");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"解包异常:{ex.Message}");
                            OnExtractionFailed($"{fileName} 处理错误:{ex.Message}");
                        }
                    }
                    var allExistingFilesAfter = GetAllNonPacFiles(directoryPath);
                    var allNewFiles = allExistingFilesAfter.Except(allExistingFilesBefore).ToList();
                    ExtractionProgress?.Invoke(this, $"处理完成，共解包{processedCount}/{pacFiles.Length}个PAC文件，总共提取出{allNewFiles.Count}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }
        private List<string> GetAllNonPacFiles(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return new List<string>();
                return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                               .Where(file => !file.EndsWith(".pac", StringComparison.OrdinalIgnoreCase))
                               .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}