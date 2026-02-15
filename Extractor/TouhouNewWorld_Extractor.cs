namespace super_toolbox
{
    public class TouhouNewWorld_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private int fileCounter = 0;
        private DdsExtractor ddsExtractor = new DdsExtractor();

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

            string sourceFolderName = new DirectoryInfo(directoryPath).Name;
            string parentDirectory = Directory.GetParent(directoryPath)?.FullName ?? directoryPath;
            string rootExtractedFolder = Path.Combine(parentDirectory, sourceFolderName);
            Directory.CreateDirectory(rootExtractedFolder);

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            ddsExtractor.ExtractionProgress += (sender, message) =>
            {
                ExtractionProgress?.Invoke(this, message);
            };

            ddsExtractor.ExtractionError += (sender, message) =>
            {
                ExtractionError?.Invoke(this, message);
            };

            ddsExtractor.FileExtracted += (sender, fileName) =>
            {
                fileCounter++;
                OnFileExtracted(fileName);
            };

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        string extension = Path.GetExtension(filePath).ToLower();

                        if (extension == ".pak")
                        {
                            await ProcessPakFileAsync(filePath, rootExtractedFolder, cancellationToken);
                        }
                        else if (extension == ".dat")
                        {
                            await ProcessDatFileAsync(filePath, rootExtractedFolder, cancellationToken);
                        }
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

        private async Task ProcessPakFileAsync(string pakFilePath, string rootExtractedFolder, CancellationToken cancellationToken)
        {
            string baseName = Path.GetFileNameWithoutExtension(pakFilePath);
            string outputDir = Path.Combine(rootExtractedFolder, baseName);
            Directory.CreateDirectory(outputDir);

            byte[] pakData = await File.ReadAllBytesAsync(pakFilePath, cancellationToken);

            int indexCount = BitConverter.ToInt32(pakData, 0x0C);
            int fileCount = 0;

            for (int i = 0; i < indexCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int indexOffset = 0x20 + i * 128;
                if (indexOffset + 16 > pakData.Length) break;

                long fileOffset = BitConverter.ToInt64(pakData, indexOffset);
                long fileSize = BitConverter.ToInt64(pakData, indexOffset + 8);

                if (fileOffset == 0 || fileSize == 0) continue;

                if (fileOffset + fileSize <= pakData.Length)
                {
                    fileCount++;
                    byte[] fileData = new byte[fileSize];
                    Array.Copy(pakData, fileOffset, fileData, 0, fileSize);

                    string outputFileName = $"{baseName}_{fileCount}.ogg";
                    string outputFilePath = Path.Combine(outputDir, outputFileName);
                    outputFilePath = GetUniqueFilePath(outputFilePath);

                    await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);

                    fileCounter++;
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取音频文件:{outputFileName}");
                }
            }
        }

        private async Task ProcessDatFileAsync(string datFilePath, string rootExtractedFolder, CancellationToken cancellationToken)
        {
            string baseName = Path.GetFileNameWithoutExtension(datFilePath);
            string outputDir = Path.Combine(rootExtractedFolder, baseName);
            Directory.CreateDirectory(outputDir);

            int extractedCount = await ddsExtractor.ProcessFileAsync(datFilePath, rootExtractedFolder, cancellationToken);
        }

        private string GetUniqueFilePath(string filePath)
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