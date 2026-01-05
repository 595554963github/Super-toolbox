using System.Diagnostics;

namespace super_toolbox
{
    public class Xbox360_iso_Extractor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static Xbox360_iso_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.exiso.exe", "exiso.exe");
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

                    foreach (var isoFilePath in isoFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string fileName = Path.GetFileName(isoFilePath);
                        string isoFileNameWithoutExt = Path.GetFileNameWithoutExtension(isoFilePath);
                        string extractDir = Path.Combine(directoryPath, isoFileNameWithoutExt);

                        ExtractionProgress?.Invoke(this, $"正在提取:{fileName}");

                        try
                        {
                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-x \"{isoFilePath}\"",
                                WorkingDirectory = directoryPath,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };

                            using (var process = Process.Start(processStartInfo))
                            {
                                if (process == null)
                                {
                                    ExtractionError?.Invoke(this, $"无法启动提取进程:{fileName}");
                                    OnExtractionFailed($"无法启动提取进程:{fileName}");
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
                                    ExtractionError?.Invoke(this, $"{fileName}提取失败,错误代码:{process.ExitCode}");
                                    OnExtractionFailed($"{fileName}提取失败,错误代码:{process.ExitCode}");
                                }
                                else
                                {
                                    ExtractionProgress?.Invoke(this, $"提取成功:{fileName}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"提取异常:{ex.Message}");
                            OnExtractionFailed($"{fileName}处理错误:{ex.Message}");
                        }
                    }

                    // 扫描所有提取的文件夹，统计实际文件数量
                    int actualFileCount = 0;
                    foreach (var isoFilePath in isoFiles)
                    {
                        string isoFileNameWithoutExt = Path.GetFileNameWithoutExtension(isoFilePath);
                        string extractDir = Path.Combine(directoryPath, isoFileNameWithoutExt);

                        if (Directory.Exists(extractDir))
                        {
                            var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                            actualFileCount += extractedFiles.Length;

                            // 触发文件提取事件
                            foreach (var extractedFile in extractedFiles)
                            {
                                OnFileExtracted(extractedFile);
                                totalFilesExtracted++;
                            }
                        }
                    }

                    // 设置总文件数
                    TotalFilesToExtract = actualFileCount;

                    ExtractionProgress?.Invoke(this, $"处理完成,共提取{actualFileCount}个文件");
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