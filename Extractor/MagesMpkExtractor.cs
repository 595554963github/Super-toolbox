using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class MagesMpkExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static string _tempExePath;

        static MagesMpkExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "mpk.exe");
            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.mpk.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的MPK工具资源未找到");
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
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
            var mpkFiles = Directory.GetFiles(directoryPath, "*.mpk", SearchOption.AllDirectories);
            if (mpkFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.mpk文件");
                OnExtractionFailed("未找到任何.mpk文件");
                return;
            }
            TotalFilesToExtract = mpkFiles.Length;
            int processedFiles = 0;
            try
            {
                foreach (var mpkFilePath in mpkFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(mpkFilePath)} ({processedFiles}/{TotalFilesToExtract})");
                    try
                    {
                        string fileDirectory = Path.GetDirectoryName(mpkFilePath) ?? string.Empty;
                        string mpkFileNameWithoutExt = Path.GetFileNameWithoutExtension(mpkFilePath);
                        string extractedFolder = Path.Combine(fileDirectory, mpkFileNameWithoutExt);

                        if (Directory.Exists(extractedFolder))
                            Directory.Delete(extractedFolder, true);
                        Directory.CreateDirectory(extractedFolder);
                        var processStartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"-e \"{mpkFilePath}\"",
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
                                ExtractionError?.Invoke(this, $"无法启动解包进程:{Path.GetFileName(mpkFilePath)}");
                                OnExtractionFailed($"无法启动解包进程:{Path.GetFileName(mpkFilePath)}");
                                continue;
                            }
                            var knownFiles = new HashSet<string>();
                            using (var fileWatcher = new FileSystemWatcher(extractedFolder))
                            {
                                fileWatcher.IncludeSubdirectories = true;
                                fileWatcher.EnableRaisingEvents = true;
                                fileWatcher.Created += (s, e) =>
                                {
                                    if (!knownFiles.Contains(e.FullPath) && !Directory.Exists(e.FullPath))
                                    {
                                        knownFiles.Add(e.FullPath);
                                        OnFileExtracted(e.FullPath);
                                        ExtractionProgress?.Invoke(this, $"已提取: {Path.GetFileName(e.FullPath)}");
                                    }
                                };
                                fileWatcher.Renamed += (s, e) =>
                                {
                                    if (!knownFiles.Contains(e.FullPath) && !Directory.Exists(e.FullPath))
                                    {
                                        knownFiles.Add(e.FullPath);
                                        OnFileExtracted(e.FullPath);
                                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(e.FullPath)}");
                                    }
                                };
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
                                        ExtractionError?.Invoke(this, e.Data);
                                    }
                                };
                                process.BeginOutputReadLine();
                                process.BeginErrorReadLine();
                                while (!process.HasExited)
                                {
                                    ThrowIfCancellationRequested(cancellationToken);
                                    await Task.Delay(100, cancellationToken);
                                }
                                fileWatcher.EnableRaisingEvents = false;
                            }
                            var allExtractedFiles = Directory.GetFiles(extractedFolder, "*", SearchOption.AllDirectories);
                            foreach (var extractedFile in allExtractedFiles)
                            {
                                if (!knownFiles.Contains(extractedFile))
                                {
                                    OnFileExtracted(extractedFile);
                                    ExtractionProgress?.Invoke(this, $"已提取: {Path.GetFileName(extractedFile)}");
                                }
                            }
                            if (process.ExitCode != 0)
                            {
                                ExtractionError?.Invoke(this, $"{Path.GetFileName(mpkFilePath)}解包失败");
                                OnExtractionFailed($"{Path.GetFileName(mpkFilePath)}解包失败");
                            }
                            else
                            {
                                ExtractionProgress?.Invoke(this, $"{Path.GetFileName(mpkFilePath)}解包完成，共提取{allExtractedFiles.Length}个文件");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{mpkFilePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{mpkFilePath}时出错:{ex.Message}");
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
        }
        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("操作已取消", cancellationToken);
            }
        }
    }
}