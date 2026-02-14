namespace super_toolbox
{
    public class SgggExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private int fileCounter = 0;

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

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mrg", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".efc", StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        await ProcessArchiveFileAsync(filePath, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,共提取{fileCounter}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ProcessArchiveFileAsync(string archiveFilePath, CancellationToken cancellationToken)
        {
            string baseName = Path.GetFileNameWithoutExtension(archiveFilePath);
            string outputDir = Path.Combine(Path.GetDirectoryName(archiveFilePath) ?? "", baseName);
            Directory.CreateDirectory(outputDir);

            byte[] content = await File.ReadAllBytesAsync(archiveFilePath, cancellationToken);

            int index = 4;
            int entryCount = 0;

            while (index + 8 <= content.Length)
            {
                uint offset = BitConverter.ToUInt32(content, index);
                uint size = BitConverter.ToUInt32(content, index + 4);

                if (offset == 0 && size == 0)
                {
                    byte[] emptyCheck = new byte[8];
                    Array.Copy(content, index, emptyCheck, 0, 8);

                    if (emptyCheck.SequenceEqual(new byte[8]))
                    {
                        break;
                    }
                }

                if (offset > 0 && size > 0 && offset + size <= content.Length)
                {
                    entryCount++;
                    byte[] fileData = new byte[size];
                    Array.Copy(content, offset, fileData, 0, size);

                    string outputFileName = $"{baseName}_{entryCount}.bin";
                    string outputFilePath = Path.Combine(outputDir, outputFileName);
                    outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                    await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);

                    fileCounter++;
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
                }

                index += 8;
            }
        }

        private async Task<string> GenerateUniqueFilePathAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;
            string newPath;

            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
                counter++;
                ThrowIfCancellationRequested(cancellationToken);
            } while (File.Exists(newPath));

            return newPath;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}