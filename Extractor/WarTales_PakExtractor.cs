namespace super_toolbox
{
    public class WarTales_PakExtractor : BaseExtractor
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
            if (string.IsNullOrEmpty(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误: 目录路径为空");
                OnExtractionFailed("错误: 目录路径为空");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误: 目录不存在: {directoryPath}");
                OnExtractionFailed($"错误: 目录不存在: {directoryPath}");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.pak", SearchOption.TopDirectoryOnly)
                .ToList();

            int totalSourceFiles = filePaths.Count;
            int processedSourceFiles = 0;
            int totalExtractedFiles = 0;

            TotalFilesToExtract = totalSourceFiles;

            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedSourceFiles++;

                ExtractionProgress?.Invoke(this, $"正在处理源文件({processedSourceFiles}/{totalSourceFiles}): {Path.GetFileName(filePath)}");

                try
                {
                    string outputPath = Path.Combine(directoryPath, "Extracted", Path.GetFileNameWithoutExtension(filePath));
                    Directory.CreateDirectory(outputPath);

                    await Task.Run(() =>
                    {
                        PakExtractor.UnpackPakFile(filePath, outputPath);
                    }, cancellationToken);

                    var extractedFiles = Directory.EnumerateFiles(outputPath, "*.*", SearchOption.AllDirectories).ToList();

                    foreach (var extractedFile in extractedFiles)
                    {
                        OnFileExtracted(extractedFile);
                        totalExtractedFiles++;
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(extractedFile)}");
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
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)} 时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{Path.GetFileName(filePath)} 时出错:{e.Message}");
                }
            }

            if (totalExtractedFiles > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共处理{totalSourceFiles}个源文件，提取出{totalExtractedFiles}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共处理{totalSourceFiles}个源文件，未找到可提取的文件");
            }

            OnExtractionCompleted();
        }
    }
}