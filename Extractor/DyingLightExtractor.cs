using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class DyingLightExtractor : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static DyingLightExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);

            _tempExePath = Path.Combine(tempDir, "DyingLight.exe");
            _tempDllPath = Path.Combine(tempDir, "zlib.net.dll");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.DyingLight.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的DyingLight.exe资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
            }

            if (!File.Exists(_tempDllPath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.zlib.net.dll"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的zlib.net.dll资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempDllPath, buffer);
                }
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var rpackFiles = Directory.GetFiles(directoryPath, "*.rpack", SearchOption.AllDirectories).ToList();

            if (rpackFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到.rpack文件");
                OnExtractionFailed("未找到.rpack文件");
                return;
            }

            TotalFilesToExtract = rpackFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            ExtractionProgress?.Invoke(this, $"找到{rpackFiles.Count}个.rpack文件，开始提取...");

            int successfullyProcessedCount = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var filePath in rpackFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(filePath);
                        string currentDir = Path.GetDirectoryName(filePath) ?? directoryPath;

                        ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                        try
                        {
                            var existingFoldersBefore = Directory.GetDirectories(currentDir, "*", SearchOption.AllDirectories)
                                .ToHashSet();

                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"\"{filePath}\"",
                                WorkingDirectory = currentDir,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };

                            using (var process = Process.Start(processStartInfo))
                            {
                                if (process == null)
                                {
                                    ExtractionError?.Invoke(this, $"无法启动处理进程:{fileName}");
                                    OnExtractionFailed($"无法启动处理进程:{fileName}");
                                    continue;
                                }

                                var fileWatcher = new FileSystemWatcher
                                {
                                    Path = currentDir,
                                    Filter = "*",
                                    IncludeSubdirectories = true,
                                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                };

                                var folderWatchers = new List<FileSystemWatcher>();
                                var extractedFiles = new HashSet<string>();

                                fileWatcher.Created += (sender, e) =>
                                {
                                    if (Directory.Exists(e.FullPath))
                                    {
                                        if (!existingFoldersBefore.Contains(e.FullPath))
                                        {
                                            var folderWatcher = WatchNewFolder(e.FullPath, extractedFiles);
                                            folderWatchers.Add(folderWatcher);
                                        }
                                    }
                                    else if (File.Exists(e.FullPath))
                                    {
                                        string? fileDir = Path.GetDirectoryName(e.FullPath);
                                        if (fileDir != null && !existingFoldersBefore.Contains(fileDir))
                                        {
                                            lock (extractedFiles)
                                            {
                                                if (extractedFiles.Add(e.FullPath))
                                                {
                                                    OnFileExtracted(e.FullPath);
                                                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(e.FullPath)}");
                                                }
                                            }
                                        }
                                    }
                                };

                                fileWatcher.EnableRaisingEvents = true;

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

                                fileWatcher.EnableRaisingEvents = false;
                                fileWatcher.Dispose();

                                foreach (var watcher in folderWatchers)
                                {
                                    watcher.EnableRaisingEvents = false;
                                    watcher.Dispose();
                                }

                                if (process.ExitCode == 0)
                                {
                                    successfullyProcessedCount++;

                                    var newFoldersAfter = Directory.GetDirectories(currentDir, "*", SearchOption.AllDirectories)
                                        .Where(f => !existingFoldersBefore.Contains(f))
                                        .ToList();

                                    int finalFileCount = 0;
                                    foreach (var newFolder in newFoldersAfter)
                                    {
                                        var filesInNewFolder = Directory.GetFiles(newFolder, "*", SearchOption.AllDirectories);
                                        finalFileCount += filesInNewFolder.Length;
                                    }

                                    if (finalFileCount > extractedFiles.Count)
                                    {
                                        int missingCount = finalFileCount - extractedFiles.Count;
                                        ExtractionProgress?.Invoke(this, $"纠正计数:补录{missingCount}个文件");

                                        foreach (var newFolder in newFoldersAfter)
                                        {
                                            var filesInNewFolder = Directory.GetFiles(newFolder, "*", SearchOption.AllDirectories);
                                            foreach (var file in filesInNewFolder)
                                            {
                                                if (extractedFiles.Add(file))
                                                {
                                                    OnFileExtracted(file);
                                                }
                                            }
                                        }
                                    }

                                    ExtractionProgress?.Invoke(this, $"处理成功:{fileName} -> {extractedFiles.Count}个文件");
                                }
                                else
                                {
                                    ExtractionError?.Invoke(this, $"{fileName}处理失败，错误代码:{process.ExitCode}");
                                    OnExtractionFailed($"{fileName}处理失败，错误代码:{process.ExitCode}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理异常:{ex.Message}");
                            OnExtractionFailed($"{fileName} 处理错误:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"处理完成:{successfullyProcessedCount}/{rpackFiles.Count}个文件成功处理");
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

        private FileSystemWatcher WatchNewFolder(string folderPath, HashSet<string> extractedFiles)
        {
            var watcher = new FileSystemWatcher
            {
                Path = folderPath,
                Filter = "*",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
            };

            watcher.Created += (sender, e) =>
            {
                if (File.Exists(e.FullPath))
                {
                    lock (extractedFiles)
                    {
                        if (extractedFiles.Add(e.FullPath))
                        {
                            OnFileExtracted(e.FullPath);
                            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(e.FullPath)}");
                        }
                    }
                }
            };

            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}