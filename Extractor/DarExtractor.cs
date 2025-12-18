using System.IO.MemoryMappedFiles;

namespace super_toolbox
{
    public class DarExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const long LARGE_FILE_THRESHOLD = 2L * 1024 * 1024 * 1024;
        private const long MEMORY_MAP_BUFFER_SIZE = 64 * 1024 * 1024;
        private const byte RIFF_HEADER_0 = 0x52; // R
        private const byte RIFF_HEADER_1 = 0x49; // I
        private const byte RIFF_HEADER_2 = 0x46; // F
        private const byte RIFF_HEADER_3 = 0x46; // F

        private int fileCounter = 0;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

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
                        string sourceFileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputDir = Path.Combine(Path.GetDirectoryName(filePath) ?? directoryPath, sourceFileName);
                        Directory.CreateDirectory(outputDir);

                        FileInfo fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length < LARGE_FILE_THRESHOLD)
                        {
                            byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                            await ExtractAt9AudioFilesAsync(content, filePath, outputDir, extractedFiles, cancellationToken);
                        }
                        else
                        {
                            await ExtractLargeFileAsync(filePath, outputDir, extractedFiles, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成，共提取{fileCounter}个音频文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ExtractAt9AudioFilesAsync(byte[] content, string filePath,
            string outputDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int position = 0;
            int fileNumber = 0;

            while (position < content.Length - 8)
            {
                ThrowIfCancellationRequested(cancellationToken);

                if (content[position] == RIFF_HEADER_0 &&
                    content[position + 1] == RIFF_HEADER_1 &&
                    content[position + 2] == RIFF_HEADER_2 &&
                    content[position + 3] == RIFF_HEADER_3)
                {
                    if (position + 8 <= content.Length)
                    {
                        int fileSize = BitConverter.ToInt32(content, position + 4);
                        long totalBlockSize = fileSize + 8;
                        if (position + totalBlockSize <= content.Length)
                        {
                            await ExtractAudioFileAsync(content, position, totalBlockSize, filePath,
                                outputDir, extractedFiles, ++fileNumber, cancellationToken);
                            position += (int)totalBlockSize;
                            continue;
                        }
                    }
                }

                position++;
            }
        }

        private async Task ExtractLargeFileAsync(string filePath, string outputDir,
                                               List<string> extractedFiles, CancellationToken cancellationToken)
        {
            long fileSize = new FileInfo(filePath).Length;
            int fileNumber = 0;

            using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
            {
                long position = 0;

                while (position < fileSize - 8)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var headerAccessor = mmf.CreateViewAccessor(position, 4, MemoryMappedFileAccess.Read))
                    {
                        byte[] headerBuffer = new byte[4];
                        headerAccessor.ReadArray(0, headerBuffer, 0, 4);

                        if (headerBuffer[0] == RIFF_HEADER_0 &&
                            headerBuffer[1] == RIFF_HEADER_1 &&
                            headerBuffer[2] == RIFF_HEADER_2 &&
                            headerBuffer[3] == RIFF_HEADER_3)
                        {
                            if (position + 8 <= fileSize)
                            {
                                using (var sizeAccessor = mmf.CreateViewAccessor(position + 4, 4, MemoryMappedFileAccess.Read))
                                {
                                    byte[] sizeBuffer = new byte[4];
                                    sizeAccessor.ReadArray(0, sizeBuffer, 0, 4);
                                    int riffFileSize = BitConverter.ToInt32(sizeBuffer, 0);

                                    long totalBlockSize = riffFileSize + 8;

                                    if (position + totalBlockSize <= fileSize)
                                    {
                                        await ExtractAudioFromMemoryMapAsync(mmf, position, totalBlockSize, filePath,
                                            outputDir, extractedFiles, ++fileNumber, cancellationToken);

                                        position += totalBlockSize;
                                        continue;
                                    }
                                }
                            }
                        }
                    }

                    position++;
                    if (position % (100 * 1024 * 1024) == 0)
                    {
                        ExtractionProgress?.Invoke(this, $"扫描进度: {position / (1024 * 1024)}MB/{fileSize / (1024 * 1024)}MB");
                    }
                }
            }
        }

        private async Task ExtractAudioFromMemoryMapAsync(MemoryMappedFile mmf, long startOffset, long size,
                                                       string sourceFilePath, string outputDir,
                                                       List<string> extractedFiles, int fileNumber,
                                                       CancellationToken cancellationToken)
        {
            if (size <= 0)
                return;

            string sourceFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string outputFileName = $"{sourceFileName}_{fileNumber:D4}.at9";
            string outputFilePath = Path.Combine(outputDir, outputFileName);
            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

            try
            {
                using (var outputStream = File.OpenWrite(outputFilePath))
                {
                    long remaining = size;
                    long currentOffset = startOffset;

                    while (remaining > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long chunkSize = Math.Min(MEMORY_MAP_BUFFER_SIZE, remaining);

                        using (var accessor = mmf.CreateViewAccessor(currentOffset, chunkSize, MemoryMappedFileAccess.Read))
                        {
                            byte[] buffer = new byte[chunkSize];
                            accessor.ReadArray(0, buffer, 0, (int)chunkSize);
                            await outputStream.WriteAsync(buffer, 0, (int)chunkSize, cancellationToken);
                        }

                        currentOffset += chunkSize;
                        remaining -= chunkSize;
                    }
                }

                extractedFiles.Add(outputFilePath);
                fileCounter++;
                OnFileExtracted(outputFilePath);

                ExtractionProgress?.Invoke(this, $"已提取音频文件:{outputFileName}");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{ex.Message}");
                OnExtractionFailed($"写入文件{outputFilePath}时出错:{ex.Message}");
            }
        }

        private async Task ExtractAudioFileAsync(byte[] content, long start, long size,
            string sourceFilePath, string outputDir, List<string> extractedFiles,
            int fileNumber, CancellationToken cancellationToken)
        {
            if (size <= 0 || start + size > content.Length)
                return;

            byte[] fileData = new byte[size];
            Array.Copy(content, (int)start, fileData, 0, (int)size);

            string sourceFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string outputFileName = $"{sourceFileName}_{fileNumber:D4}.at9";
            string outputFilePath = Path.Combine(outputDir, outputFileName);
            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

            try
            {
                await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);
                extractedFiles.Add(outputFilePath);
                fileCounter++;
                OnFileExtracted(outputFilePath);

                ExtractionProgress?.Invoke(this, $"已提取音频文件:{outputFileName}");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{ex.Message}");
                OnExtractionFailed($"写入文件{outputFilePath}时出错:{ex.Message}");
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