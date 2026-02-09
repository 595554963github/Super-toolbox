using System.Diagnostics;

namespace super_toolbox
{
    public class IdeaFactory_PacExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempExePath;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _processedFiles = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        private System.Threading.CancellationTokenSource? _monitorCancellationTokenSource;

        static IdeaFactory_PacExtractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.pactool.exe", "pactool.exe");
        }

        public override async System.Threading.Tasks.Task ExtractAsync(string directoryPath, System.Threading.CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var pacFiles = Directory.GetFiles(directoryPath, "*.pac", System.IO.SearchOption.AllDirectories);
            if (pacFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.pac文件");
                OnExtractionFailed("未找到任何.pac文件");
                return;
            }

            _processedFiles.Clear();
            _monitorCancellationTokenSource = new System.Threading.CancellationTokenSource();

            var linkedTokenSource = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _monitorCancellationTokenSource.Token);

            int processedPacFiles = 0;

            try
            {
                foreach (var pacFilePath in pacFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedPacFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(pacFilePath)} ({processedPacFiles}/{pacFiles.Length})");

                    try
                    {
                        string fileDirectory = Path.GetDirectoryName(pacFilePath) ?? string.Empty;
                        string pacFileNameWithoutExt = Path.GetFileNameWithoutExtension(pacFilePath);
                        string extractedFolder = Path.Combine(fileDirectory, pacFileNameWithoutExt);

                        if (Directory.Exists(extractedFolder))
                        {
                            Directory.Delete(extractedFolder, true);
                        }

                        Directory.CreateDirectory(extractedFolder);

                        var monitorTask = StartDirectoryMonitor(extractedFolder, linkedTokenSource.Token);

                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-x \"{pacFilePath}\"",
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
                                    throw new Exception($"无法启动解包进程:{Path.GetFileName(pacFilePath)}");
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
                                    throw new Exception($"{Path.GetFileName(pacFilePath)}解包失败,错误代码:{process.ExitCode}");
                                }
                            }
                        }, cancellationToken);

                        await System.Threading.Tasks.Task.Delay(500);

                        var monitorTokenSource = _monitorCancellationTokenSource;
                        if (monitorTokenSource != null)
                        {
                            monitorTokenSource.Cancel();
                        }

                        try
                        {
                            await monitorTask;
                        }
                        catch (OperationCanceledException) { }

                        if (monitorTokenSource != null)
                        {
                            monitorTokenSource.Dispose();
                            _monitorCancellationTokenSource = null;
                        }

                        var allExtractedFiles = Directory.GetFiles(extractedFolder, "*", System.IO.SearchOption.AllDirectories);
                        ExtractionProgress?.Invoke(this, $"{Path.GetFileName(pacFilePath)}解包完成,共提取了{allExtractedFiles.Length}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{pacFilePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{pacFilePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成，总提取文件数:{ExtractedFileCount}");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            finally
            {
                var monitorTokenSource = _monitorCancellationTokenSource;
                if (monitorTokenSource != null)
                {
                    try
                    {
                        monitorTokenSource.Cancel();
                        monitorTokenSource.Dispose();
                    }
                    catch { }
                    _monitorCancellationTokenSource = null;
                }
            }
        }

        private async System.Threading.Tasks.Task StartDirectoryMonitor(string directoryPath, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                System.Collections.Generic.HashSet<string> lastFiles = new System.Collections.Generic.HashSet<string>();

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!Directory.Exists(directoryPath))
                        {
                            await System.Threading.Tasks.Task.Delay(500, cancellationToken);
                            continue;
                        }

                        var currentFiles = Directory.GetFiles(directoryPath, "*", System.IO.SearchOption.AllDirectories);

                        foreach (var file in currentFiles)
                        {
                            if (!lastFiles.Contains(file) && !_processedFiles.ContainsKey(file))
                            {
                                _processedFiles[file] = true;
                                OnFileExtracted(file);
                                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(file)}");
                            }
                        }

                        lastFiles = new System.Collections.Generic.HashSet<string>(currentFiles);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }

                    await System.Threading.Tasks.Task.Delay(100, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
