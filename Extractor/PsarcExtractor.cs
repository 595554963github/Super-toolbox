using System.Diagnostics;

namespace super_toolbox
{
    public class PsarcExtractor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static PsarcExtractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.psarc.exe", "psarc.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:选择的目录不存在");
                OnExtractionFailed("错误:选择的目录不存在");
                return;
            }

            var psarcFiles = Directory.EnumerateFiles(directoryPath, "*.psarc", SearchOption.AllDirectories).ToList();
            var pakFiles = Directory.EnumerateFiles(directoryPath, "*.pak", SearchOption.AllDirectories).ToList();
            var archiveFiles = psarcFiles.Concat(pakFiles).ToList();

            if (archiveFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.psarc或.pak文件");
                OnExtractionFailed("未找到任何.psarc或.pak文件");
                return;
            }

            TotalFilesToExtract = archiveFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            ExtractionProgress?.Invoke(this, $"找到{archiveFiles.Count}个档案文件,开始解包...");

            int extractedArchiveCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var originalArchivePath in archiveFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileDirectory = Path.GetDirectoryName(originalArchivePath) ?? string.Empty;
                        string fileName = Path.GetFileName(originalArchivePath);
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalArchivePath);

                        string outputDirectory = Path.Combine(fileDirectory, fileNameWithoutExt);

                        if (!Directory.Exists(outputDirectory))
                        {
                            Directory.CreateDirectory(outputDirectory);
                        }

                        string tempArchivePath = Path.Combine(outputDirectory, fileName);

                        try
                        {
                            ExtractionProgress?.Invoke(this, $"正在处理:{fileName}->{fileNameWithoutExt}文件夹");

                            File.Copy(originalArchivePath, tempArchivePath, true);

                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"extract \"{tempArchivePath}\"",
                                WorkingDirectory = outputDirectory,
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
                                        ExtractionProgress?.Invoke(this, $"[{fileName}] {e.Data}");

                                        if (e.Data.Contains("Extracting") || e.Data.Contains("extracting"))
                                        {
                                            totalExtractedFiles++;
                                            OnFileExtracted(e.Data);
                                        }
                                    }
                                };

                                process.ErrorDataReceived += (sender, e) =>
                                {
                                    if (!string.IsNullOrEmpty(e.Data))
                                    {
                                        ExtractionError?.Invoke(this, $"[{fileName}] 错误:{e.Data}");
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
                                    ExtractionProgress?.Invoke(this, $"解包成功:{fileName}->{fileNameWithoutExt}文件夹");
                                    extractedArchiveCount++;

                                    if (Directory.Exists(outputDirectory))
                                    {
                                        var extractedFiles = Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)
                                            .Where(f => !f.Equals(tempArchivePath, StringComparison.OrdinalIgnoreCase))
                                            .ToList();
                                        ExtractionProgress?.Invoke(this, $"[{fileName}]已提取{extractedFiles.Count}个文件到{fileNameWithoutExt} 文件夹");
                                    }
                                }
                            }

                            if (File.Exists(tempArchivePath))
                            {
                                File.Delete(tempArchivePath);
                                ExtractionProgress?.Invoke(this, $"[{fileName}]已清理临时备份文件");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"[{fileName}] 处理异常:{ex.Message}");
                            OnExtractionFailed($"{fileName}处理错误:{ex.Message}");

                            if (File.Exists(tempArchivePath))
                            {
                                try { File.Delete(tempArchivePath); } catch { }
                            }
                        }

                        ExtractionProgress?.Invoke(this, "----------------------------------------");
                    }

                    ExtractionProgress?.Invoke(this, $"处理完成,成功解包{extractedArchiveCount}个档案文件,共提取出{totalExtractedFiles}个文件");
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
