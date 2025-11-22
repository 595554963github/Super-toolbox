namespace super_toolbox
{
    public class DyingLightExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

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

            var rpackFiles = Directory.EnumerateFiles(directoryPath, "*.rpack", SearchOption.AllDirectories)
                .ToList();

            if (rpackFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"未找到.rpack文件");
                OnExtractionFailed($"未找到.rpack文件");
                return;
            }

            TotalFilesToExtract = rpackFiles.Count;
            ExtractionProgress?.Invoke(this, $"找到{rpackFiles.Count}个.rpack文件，开始提取...");

            int processedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                foreach (var filePath in rpackFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    processedCount++;
                    string fileName = Path.GetFileName(filePath);
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{rpackFiles.Count}): {fileName}");

                    try
                    {
                        int extractedCount = await ExtractRP6LWithAccurateCounting(filePath, cancellationToken);
                        totalExtractedFiles += extractedCount;

                        ExtractionProgress?.Invoke(this, $"{fileName}提取完成，共提取{extractedCount}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        ExtractionError?.Invoke(this, "提取操作已取消");
                        OnExtractionFailed("提取操作已取消");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{fileName}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"所有.rpack文件处理完成，总共提取{totalExtractedFiles}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程中出错:{ex.Message}");
                OnExtractionFailed($"提取过程中出错:{ex.Message}");
            }
        }

        private async Task<int> ExtractRP6LWithAccurateCounting(string filePath, CancellationToken cancellationToken)
        {
            string extractFolder = Path.Combine(
                Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(filePath) + "_extracted");

            if (Directory.Exists(extractFolder))
            {
                Directory.Delete(extractFolder, true);
                await Task.Delay(300, cancellationToken);
            }

            var extractedFiles = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
            int lastReportedCount = 0;

            using (var fileWatcher = new FileSystemWatcher())
            {
                fileWatcher.Path = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
                fileWatcher.Filter = "*.*";
                fileWatcher.IncludeSubdirectories = true;
                fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
                fileWatcher.InternalBufferSize = 65536;

                var extractCompleted = new TaskCompletionSource<bool>();

                void OnFileCreated(object sender, FileSystemEventArgs e)
                {
                    if (File.Exists(e.FullPath) && IsFileInExtractFolder(e.FullPath, extractFolder))
                    {
                        if (extractedFiles.TryAdd(e.FullPath, true))
                        {
                            base.OnFileExtracted(e.FullPath);
                            UpdateProgressDisplay();
                        }
                    }
                }

                void OnFileChanged(object sender, FileSystemEventArgs e)
                {
                    if (File.Exists(e.FullPath) && IsFileInExtractFolder(e.FullPath, extractFolder))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(e.FullPath);
                            if (fileInfo.Length > 0)
                            {
                                if (extractedFiles.TryAdd(e.FullPath, true))
                                {
                                    base.OnFileExtracted(e.FullPath);
                                    UpdateProgressDisplay();
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                void UpdateProgressDisplay()
                {
                    int currentCount = extractedFiles.Count;
                    if (currentCount > lastReportedCount)
                    {
                        lastReportedCount = currentCount;
                        ExtractionProgress?.Invoke(this, $"已提取文件:{currentCount}个");
                    }
                }

                async Task StartPolling()
                {
                    while (!extractCompleted.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cancellationToken);

                        try
                        {
                            if (Directory.Exists(extractFolder))
                            {
                                var currentFiles = Directory.GetFiles(extractFolder, "*.*", SearchOption.AllDirectories);
                                int newFilesCount = 0;

                                foreach (var file in currentFiles)
                                {
                                    if (extractedFiles.TryAdd(file, true))
                                    {
                                        newFilesCount++;
                                        base.OnFileExtracted(file);

                                        if (newFilesCount <= 5)
                                        {
                                            string relativePath = GetRelativePath(file, Path.GetDirectoryName(filePath) ?? string.Empty);
                                            ExtractionProgress?.Invoke(this, $"发现文件:{relativePath}");
                                        }
                                    }
                                }

                                if (newFilesCount > 0)
                                {
                                    int totalCount = extractedFiles.Count;
                                    ExtractionProgress?.Invoke(this, $"发现{newFilesCount}个新文件，总计:{totalCount}个文件");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"轮询错误:{ex.Message}");
                        }
                    }
                }

                fileWatcher.Created += OnFileCreated;
                fileWatcher.Changed += OnFileChanged;
                fileWatcher.EnableRaisingEvents = true;

                try
                {
                    var pollingTask = StartPolling();

                    var extractTask = Task.Run(() =>
                    {
                        return DyingLight.DyingLight.ExtractRP6L(filePath);
                    }, cancellationToken);

                    var timeoutTask = Task.Delay(600000, cancellationToken);
                    var completedTask = await Task.WhenAny(extractTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        throw new TimeoutException($"文件{Path.GetFileName(filePath)}提取超时");
                    }

                    bool success = await extractTask;
                    if (!success)
                    {
                        throw new Exception($"文件{Path.GetFileName(filePath)}提取失败");
                    }

                    extractCompleted.TrySetResult(true);

                    await pollingTask;

                    int finalCount = extractedFiles.Count;
                    ExtractionProgress?.Invoke(this, $"{Path.GetFileName(filePath)}提取完成，最终数量:{finalCount}个文件");

                    return finalCount;
                }
                finally
                {
                    fileWatcher.EnableRaisingEvents = false;
                    fileWatcher.Created -= OnFileCreated;
                    fileWatcher.Changed -= OnFileChanged;
                }
            }
        }

        private bool IsFileInExtractFolder(string filePath, string extractFolder)
        {
            string? fileDir = Path.GetDirectoryName(filePath);
            return !string.IsNullOrEmpty(fileDir) &&
                   fileDir.StartsWith(extractFolder, StringComparison.OrdinalIgnoreCase);
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            if (string.IsNullOrEmpty(basePath))
                return Path.GetFileName(fullPath);

            try
            {
                Uri fullUri = new Uri(fullPath);
                Uri baseUri = new Uri(basePath + Path.DirectorySeparatorChar);

                string relativePath = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
                return relativePath.Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return Path.GetFileName(fullPath);
            }
        }
    }
}
