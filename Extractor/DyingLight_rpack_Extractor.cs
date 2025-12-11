namespace super_toolbox
{
    public class DyingLight_rpack_Extractor : BaseExtractor
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
                Path.GetFileNameWithoutExtension(filePath));

            if (Directory.Exists(extractFolder))
            {
                Directory.Delete(extractFolder, true);
                await Task.Delay(300, cancellationToken);
            }

            var extractedFiles = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
            int lastReportedCount = 0;

            Directory.CreateDirectory(extractFolder);

            using (var fileWatcher = new FileSystemWatcher())
            {
                fileWatcher.Path = extractFolder;
                fileWatcher.Filter = "*.*";
                fileWatcher.IncludeSubdirectories = true;
                fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime;
                fileWatcher.InternalBufferSize = 65536;

                var extractCompleted = new TaskCompletionSource<bool>();

                void OnFileCreated(object sender, FileSystemEventArgs e)
                {
                    if (File.Exists(e.FullPath))
                    {
                        if (extractedFiles.TryAdd(e.FullPath, true))
                        {
                            base.OnFileExtracted(e.FullPath);
                            UpdateProgressDisplay();

                            if (extractedFiles.Count % 10 == 0 || extractedFiles.Count <= 5)
                            {
                                string relativePath = GetRelativePath(e.FullPath, extractFolder);
                                ExtractionProgress?.Invoke(this, $"提取文件:{relativePath}");
                            }
                        }
                    }
                }

                void OnFileChanged(object sender, FileSystemEventArgs e)
                {
                    if (File.Exists(e.FullPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(e.FullPath);
                            if (fileInfo.Length > 0 && extractedFiles.TryAdd(e.FullPath, true))
                            {
                                base.OnFileExtracted(e.FullPath);
                                UpdateProgressDisplay();
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
                    if (currentCount != lastReportedCount)
                    {
                        lastReportedCount = currentCount;
                        ExtractionProgress?.Invoke(this, $"已提取文件:{currentCount}个");
                    }
                }

                async Task StartPolling()
                {
                    int consecutiveNoChangeCount = 0;

                    while (!extractCompleted.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(2000, cancellationToken);

                        try
                        {
                            if (Directory.Exists(extractFolder))
                            {
                                var allFiles = Directory.GetFiles(extractFolder, "*.*", SearchOption.AllDirectories);
                                int previousCount = extractedFiles.Count;

                                foreach (var file in allFiles)
                                {
                                    if (File.Exists(file) && extractedFiles.TryAdd(file, true))
                                    {
                                        base.OnFileExtracted(file);
                                    }
                                }

                                int newFilesCount = extractedFiles.Count - previousCount;

                                if (newFilesCount > 0)
                                {
                                    consecutiveNoChangeCount = 0;
                                    int totalCount = extractedFiles.Count;
                                    ExtractionProgress?.Invoke(this, $"轮询发现{newFilesCount}个新文件，总计:{totalCount}个文件");
                                }
                                else
                                {
                                    consecutiveNoChangeCount++;
                                    if (consecutiveNoChangeCount >= 3)
                                    {
                                        ExtractionProgress?.Invoke(this, $"文件数量稳定:{extractedFiles.Count}个文件");
                                    }
                                }

                                UpdateProgressDisplay();
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

                    await Task.Delay(3000, cancellationToken);

                    if (Directory.Exists(extractFolder))
                    {
                        var finalFiles = Directory.GetFiles(extractFolder, "*.*", SearchOption.AllDirectories);
                        int finalNewCount = 0;

                        foreach (var file in finalFiles)
                        {
                            if (File.Exists(file) && extractedFiles.TryAdd(file, true))
                            {
                                finalNewCount++;
                                base.OnFileExtracted(file);
                            }
                        }

                        if (finalNewCount > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"最终扫描发现{finalNewCount}个文件");
                        }
                    }

                    extractCompleted.TrySetResult(true);
                    await pollingTask;

                    int finalCount = extractedFiles.Count;
                    ExtractionProgress?.Invoke(this, $"{Path.GetFileName(filePath)}提取完成，最终数量:{finalCount}个文件");

                    return finalCount;
                }
                catch
                {
                    extractCompleted.TrySetResult(false);
                    throw;
                }
                finally
                {
                    fileWatcher.EnableRaisingEvents = false;
                    fileWatcher.Created -= OnFileCreated;
                    fileWatcher.Changed -= OnFileChanged;
                }
            }
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
                try
                {
                    string full = Path.GetFullPath(fullPath);
                    string baseFull = Path.GetFullPath(basePath);

                    if (full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                    {
                        return full.Substring(baseFull.Length).TrimStart(Path.DirectorySeparatorChar);
                    }
                    return Path.GetFileName(fullPath);
                }
                catch
                {
                    return Path.GetFileName(fullPath);
                }
            }
        }
    }
}
