using System.Diagnostics;

namespace super_toolbox
{
    public class DtsExtractor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        static DtsExtractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.SRPG_Unpacker.exe", "SRPG_Unpacker.exe");
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:选择的目录不存在");
                OnExtractionFailed("错误:选择的目录不存在");
                return;
            }

            var dtsFiles = Directory.GetFiles(directoryPath, "*.dts", SearchOption.AllDirectories);

            if (dtsFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.dts文件");
                OnExtractionFailed("未找到任何.dts文件");
                return;
            }
            TotalFilesToExtract = dtsFiles.Length;

            ExtractionProgress?.Invoke(this, $"找到{dtsFiles.Length}个.dts文件，开始解包...");

            int successfullyProcessedCount = 0;
            int totalExtractedFiles = 0;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var dtsFilePath in dtsFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileDirectory = Path.GetDirectoryName(dtsFilePath) ?? string.Empty;
                        string fileName = Path.GetFileName(dtsFilePath);
                        string dtsFileNameWithoutExt = Path.GetFileNameWithoutExtension(dtsFilePath);

                        ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                        try
                        {
                            var filesBefore = Directory.GetFiles(fileDirectory, "*", SearchOption.AllDirectories)
                                .ToHashSet();

                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"\"{dtsFilePath}\"",
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
                                        string errorMessage = e.Data;
                                        bool shouldIgnore = errorMessage.Contains("IMPLEMENT ME") ||
                                                           errorMessage.Contains("EDITDATA::init") ||
                                                           errorMessage.Contains("struct UNITFUSION");

                                        if (!shouldIgnore)
                                        {
                                            ExtractionError?.Invoke(this, $"错误:{errorMessage}");
                                        }
                                    }
                                };
                                process.BeginOutputReadLine();
                                process.BeginErrorReadLine();
                                process.WaitForExit();
                                var filesAfter = Directory.GetFiles(fileDirectory, "*", SearchOption.AllDirectories)
                                    .ToHashSet();
                                var newFiles = filesAfter.Except(filesBefore).ToList();

                                if (newFiles.Count > 0)
                                {
                                    successfullyProcessedCount++;
                                    totalExtractedFiles += newFiles.Count;

                                    ExtractionProgress?.Invoke(this, $"成功提取{newFiles.Count}个文件:");

                                    foreach (var newFile in newFiles)
                                    {
                                        string relativePath = Path.GetRelativePath(fileDirectory, newFile);
                                        ExtractionProgress?.Invoke(this, $"{relativePath}");
                                        OnFileExtracted(newFile); 
                                    }
                                }
                                else
                                {
                                    ExtractionError?.Invoke(this, $"{fileName}处理完成但未生成新文件");
                                }

                                if (process.ExitCode != 0 && newFiles.Count == 0)
                                {
                                    ExtractionError?.Invoke(this, $"{fileName}解包失败，错误代码:{process.ExitCode}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理异常:{ex.Message}");
                        }
                    }
                    ExtractionProgress?.Invoke(this, $"处理完成:{successfullyProcessedCount}/{dtsFiles.Length}个DTS文件成功处理");
                    ExtractionProgress?.Invoke(this, $"总共提取出{totalExtractedFiles}个文件");
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