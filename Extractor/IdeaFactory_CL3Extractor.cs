using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class IdeaFactory_CL3Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static string _tempExePath;
        private static string _tempDllPath;

        static IdeaFactory_CL3Extractor()
        {
            Directory.CreateDirectory(TempDllDirectory);
            _tempExePath = Path.Combine(TempDllDirectory, "Multi-Extractor.exe");
            _tempDllPath = Path.Combine(TempDllDirectory, "File Formats.dll");

            if (!File.Exists(_tempExePath))
            {
                ReleaseEmbeddedResource("embedded.Multi-Extractor.exe", _tempExePath);
            }
            if (!File.Exists(_tempDllPath))
            {
                ReleaseEmbeddedResource("embedded.File Formats.dll", _tempDllPath);
            }
        }
        private static void ReleaseEmbeddedResource(string resourceName, string targetPath)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException($"找不到嵌入资源:{resourceName}");
                File.WriteAllBytes(targetPath, ReadAllBytes(stream));
            }
        }
        private static byte[] ReadAllBytes(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
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

            var cl3Files = Directory.GetFiles(directoryPath, "*.cl3", SearchOption.AllDirectories);
            if (cl3Files.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.cl3文件");
                OnExtractionFailed("未找到任何.cl3文件");
                return;
            }
            TotalFilesToExtract = cl3Files.Length;
            int processedFiles = 0;
            try
            {
                foreach (var cl3FilePath in cl3Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(cl3FilePath)} ({processedFiles}/{TotalFilesToExtract})");
                    try
                    {
                        string? parentDir = Path.GetDirectoryName(cl3FilePath);
                        if (string.IsNullOrEmpty(parentDir))
                        {
                            ExtractionError?.Invoke(this, $"无效路径: {cl3FilePath}");
                            OnExtractionFailed($"无效路径: {cl3FilePath}");
                            continue;
                        }
                        string cl3FileNameWithoutExt = Path.GetFileNameWithoutExtension(cl3FilePath);
                        string extractedFolder = Path.Combine(parentDir, cl3FileNameWithoutExt);
                        if (Directory.Exists(extractedFolder))
                            Directory.Delete(extractedFolder, true);
                        Directory.CreateDirectory(extractedFolder);
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"\"{cl3FilePath}\"",
                            WorkingDirectory = parentDir,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            Environment = { ["PATH"] = $"{TempDllDirectory};{Environment.GetEnvironmentVariable("PATH")}" }
                        };
                        using (var process = new Process { StartInfo = processInfo })
                        {
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
                                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(e.FullPath)}");
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
                                process.Start();
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
                                ExtractionError?.Invoke(this, $"{Path.GetFileName(cl3FilePath)}解包失败");
                                OnExtractionFailed($"{Path.GetFileName(cl3FilePath)}解包失败");
                            }
                            else
                            {
                                ExtractionProgress?.Invoke(this, $"{Path.GetFileName(cl3FilePath)}解包完成，共提取{allExtractedFiles.Length}个文件");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{cl3FilePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{cl3FilePath}时出错:{ex.Message}");
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
    }
}