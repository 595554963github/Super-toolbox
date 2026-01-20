using System.Diagnostics;

namespace super_toolbox
{
    public class PBG3_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempExePath;

        static PBG3_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.THUnpacker.exe", "THUnpacker.exe");
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:选择的目录不存在");
                OnExtractionFailed("错误:选择的目录不存在");
                return;
            }

            var datFiles = Directory.EnumerateFiles(directoryPath, "*.DAT", SearchOption.AllDirectories)
                .ToList();

            if (datFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.DAT文件");
                OnExtractionFailed("未找到任何.DAT文件");
                return;
            }

            TotalFilesToExtract = datFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            ExtractionProgress?.Invoke(this, $"找到{datFiles.Count}个.DAT文件,开始解包...");

            int processedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var datFilePath in datFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileDirectory = Path.GetDirectoryName(datFilePath) ?? string.Empty;
                        string fileName = Path.GetFileName(datFilePath);

                        ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                        try
                        {
                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"\"{datFilePath}\"",
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
                                    ExtractionError?.Invoke(this, $"{fileName}解包失败,错误代码:{process.ExitCode}");
                                    OnExtractionFailed($"{fileName}解包失败,错误代码:{process.ExitCode}");
                                }
                                else
                                {
                                    ExtractionProgress?.Invoke(this, $"解包成功:{fileName}");
                                    processedCount++;

                                    string baseName = Path.GetFileNameWithoutExtension(datFilePath);
                                    string extractDir = Path.Combine(fileDirectory, baseName);

                                    if (Directory.Exists(extractDir))
                                    {
                                        var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                                        totalExtractedFiles += extractedFiles.Length;

                                        foreach (var extractedFile in extractedFiles)
                                        {
                                            string relativePath = Path.GetRelativePath(extractDir, extractedFile);
                                            ExtractionProgress?.Invoke(this, $"已提取:{relativePath}");
                                            OnFileExtracted(extractedFile);
                                        }
                                    }
                                    else
                                    {
                                        ExtractionProgress?.Invoke(this, $"警告:未找到解包文件夹{baseName}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"解包异常:{ex.Message}");
                            OnExtractionFailed($"{fileName} 处理错误:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"处理完成,共处理{processedCount}个文件,提取{totalExtractedFiles}个文件");
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
    }
}