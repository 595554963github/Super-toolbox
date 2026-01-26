namespace super_toolbox
{
    public class NarExtractor : BaseExtractor
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
            List<string> extractedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var narFiles = Directory.EnumerateFiles(directoryPath, "*.nar", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = narFiles.Count;

            foreach (var narFile in narFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(narFile)}");

                try
                {
                    using (var archive = new Nexon.NexonArchive())
                    {
                        archive.Load(narFile, false);

                        foreach (var entry in archive.FileEntries)
                        {
                            ThrowIfCancellationRequested(cancellationToken);

                            string relativePath = entry.Path;
                            if (relativePath.StartsWith("/"))
                            {
                                relativePath = relativePath.Substring(1);
                            }

                            string outputPath = Path.Combine(extractedDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                            string? outputDir = Path.GetDirectoryName(outputPath);

                            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                            {
                                Directory.CreateDirectory(outputDir);
                            }

                            using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                            {
                                long bytesExtracted = entry.Extract(fs);
                                fs.Flush();

                                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                                {
                                    File.SetLastWriteTime(outputPath, entry.LastModifiedTime);
                                    extractedFiles.Add(outputPath);
                                    OnFileExtracted(outputPath);
                                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{narFile}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{narFile}时出错:{e.Message}");
                }
            }

            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到NAR文件");
            }
            OnExtractionCompleted();
        }
    }
}