using System.IO.MemoryMappedFiles;

namespace super_toolbox
{
    public class P5S_IDRK_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] IDRK_SIGNATURE = { 0x49, 0x44, 0x52, 0x4B, 0x30, 0x30, 0x30, 0x30 };
        private const long LARGE_FILE_THRESHOLD = 2L * 1024 * 1024 * 1024;
        private const long MEMORY_MAP_BUFFER_SIZE = 256 * 1024 * 1024;
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

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                                     .Where(f => f.EndsWith(".rdb.bin", StringComparison.OrdinalIgnoreCase) ||
                                                 f.EndsWith(".rdb.bin_0", StringComparison.OrdinalIgnoreCase))
                                     .ToList();
            TotalFilesToExtract = filePaths.Count;

            int processed = 0;
            fileCounter = 0;

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                processed++;

                ExtractionProgress?.Invoke(this, $"正在处理文件({processed}/{TotalFilesToExtract}):{Path.GetFileName(filePath)}");

                try
                {
                    FileInfo fi = new FileInfo(filePath);

                    string fileNameWithExtension = Path.GetFileName(filePath);
                    string outputDir = Path.Combine(
                        Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory,
                        $"{fileNameWithExtension}_输出目录"
                    );
                    Directory.CreateDirectory(outputDir);

                    int extractedCount = 0;

                    if (fi.Length < LARGE_FILE_THRESHOLD)
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        extractedCount = ProcessNormalFile(content, filePath, outputDir);
                    }
                    else
                    {
                        extractedCount = await ProcessLargeFileWithMemoryMapAsync(filePath, outputDir, cancellationToken);
                    }

                    fileCounter += extractedCount;
                    ExtractionProgress?.Invoke(this, $"{Path.GetFileName(filePath)}提取完成,共提取{extractedCount}个文件");
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"处理完成,总共提取{fileCounter}个IDRK文件");
            OnExtractionCompleted();
        }

        private int ProcessNormalFile(byte[] content, string sourceFilePath, string outputDir)
        {
            int extractedCount = 0;
            int position = 0;
            int fileIndex = 1;

            string fileName = Path.GetFileName(sourceFilePath);

            while (position <= content.Length - 12)
            {
                if (content[position] == 0x49 && content[position + 1] == 0x44 &&
                    content[position + 2] == 0x52 && content[position + 3] == 0x4B &&
                    content[position + 4] == 0x30 && content[position + 5] == 0x30 &&
                    content[position + 6] == 0x30 && content[position + 7] == 0x30)
                {
                    int chunkSize = BitConverter.ToInt32(content, position + 8);
                    if (chunkSize <= 0)
                    {
                        position++;
                        continue;
                    }

                    int totalSize = chunkSize;
                    int endPos = position + totalSize;

                    if (endPos > content.Length)
                    {
                        position++;
                        continue;
                    }

                    string outputFileName = $"{fileName}_{fileIndex:D4}.idrk";
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    try
                    {
                        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, false);
                        fs.Write(content, position, totalSize);

                        extractedCount++;
                        fileIndex++;
                        OnFileExtracted(outputPath);

                        position = endPos;
                        continue;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"写入文件{outputFileName}时出错:{ex.Message}");
                        position++;
                    }
                }
                position++;
            }

            return extractedCount;
        }

        private async Task<int> ProcessLargeFileWithMemoryMapAsync(string filePath, string outputDir, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            int fileIndex = 1;
            long fileSize = new FileInfo(filePath).Length;
            string fileName = Path.GetFileName(filePath);

            long currentPosition = 0;
            byte[] overlapBuffer = new byte[0];
            bool lastBufferHadPartialMatch = false;

            using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
            {
                while (currentPosition < fileSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    long bufferSize = Math.Min(MEMORY_MAP_BUFFER_SIZE, fileSize - currentPosition);
                    if (bufferSize < 12 && overlapBuffer.Length == 0) break;

                    byte[] currentBuffer;

                    if (lastBufferHadPartialMatch && overlapBuffer.Length > 0)
                    {
                        using (var accessor = mmf.CreateViewAccessor(currentPosition - overlapBuffer.Length, bufferSize + overlapBuffer.Length, MemoryMappedFileAccess.Read))
                        {
                            currentBuffer = new byte[bufferSize + overlapBuffer.Length];
                            accessor.ReadArray(0, currentBuffer, 0, currentBuffer.Length);
                        }
                    }
                    else
                    {
                        using (var accessor = mmf.CreateViewAccessor(currentPosition, bufferSize, MemoryMappedFileAccess.Read))
                        {
                            currentBuffer = new byte[bufferSize];
                            accessor.ReadArray(0, currentBuffer, 0, currentBuffer.Length);
                        }
                    }

                    int localPos = 0;
                    bool foundInThisBuffer = false;

                    while (localPos <= currentBuffer.Length - 12)
                    {
                        if (currentBuffer[localPos] == 0x49 && currentBuffer[localPos + 1] == 0x44 &&
                            currentBuffer[localPos + 2] == 0x52 && currentBuffer[localPos + 3] == 0x4B &&
                            currentBuffer[localPos + 4] == 0x30 && currentBuffer[localPos + 5] == 0x30 &&
                            currentBuffer[localPos + 6] == 0x30 && currentBuffer[localPos + 7] == 0x30)
                        {
                            int chunkSize = BitConverter.ToInt32(currentBuffer, localPos + 8);
                            if (chunkSize <= 0)
                            {
                                localPos++;
                                continue;
                            }

                            int totalSize = chunkSize;
                            long globalStartPos;

                            if (lastBufferHadPartialMatch && overlapBuffer.Length > 0)
                            {
                                globalStartPos = currentPosition - overlapBuffer.Length + localPos;
                            }
                            else
                            {
                                globalStartPos = currentPosition + localPos;
                            }

                            long globalEndPos = globalStartPos + totalSize;

                            if (globalEndPos > fileSize)
                            {
                                localPos++;
                                continue;
                            }

                            int bufferRelativeEndPos;
                            if (lastBufferHadPartialMatch && overlapBuffer.Length > 0)
                            {
                                bufferRelativeEndPos = localPos + totalSize;
                            }
                            else
                            {
                                bufferRelativeEndPos = localPos + totalSize;
                            }

                            if (bufferRelativeEndPos > currentBuffer.Length)
                            {
                                lastBufferHadPartialMatch = true;
                                overlapBuffer = new byte[Math.Min(64, totalSize - (currentBuffer.Length - localPos))];
                                Array.Copy(currentBuffer, localPos, overlapBuffer, 0, overlapBuffer.Length);
                                localPos = currentBuffer.Length;
                                break;
                            }

                            string outputFileName = $"{fileName}_{fileIndex:D4}.idrk";
                            string outputPath = Path.Combine(outputDir, outputFileName);

                            try
                            {
                                using (var chunkAccessor = mmf.CreateViewAccessor(globalStartPos, totalSize, MemoryMappedFileAccess.Read))
                                {
                                    byte[] chunkData = new byte[totalSize];
                                    chunkAccessor.ReadArray(0, chunkData, 0, totalSize);

                                    await File.WriteAllBytesAsync(outputPath, chunkData, cancellationToken);
                                }

                                extractedCount++;
                                fileIndex++;
                                OnFileExtracted(outputPath);
                                foundInThisBuffer = true;

                                localPos += totalSize;
                                lastBufferHadPartialMatch = false;
                                overlapBuffer = new byte[0];
                                continue;
                            }
                            catch (Exception ex)
                            {
                                ExtractionError?.Invoke(this, $"写入文件{outputFileName}时出错:{ex.Message}");
                                localPos++;
                            }
                        }
                        localPos++;
                    }

                    if (lastBufferHadPartialMatch && overlapBuffer.Length > 0)
                    {
                        currentPosition += bufferSize - overlapBuffer.Length;
                    }
                    else
                    {
                        currentPosition += bufferSize;
                        overlapBuffer = new byte[0];
                        lastBufferHadPartialMatch = false;
                    }

                    if (fileSize > 0)
                    {
                        int progressPercentage = (int)(currentPosition * 100 / fileSize);
                        ExtractionProgress?.Invoke(this, $"处理大文件:{Path.GetFileName(filePath)} - 进度{progressPercentage}%");
                    }

                    if (foundInThisBuffer && !lastBufferHadPartialMatch)
                    {
                        int checkStart = Math.Max(0, currentBuffer.Length - 11);
                        for (int i = checkStart; i < currentBuffer.Length - 7; i++)
                        {
                            if (currentBuffer[i] == 0x49 && currentBuffer[i + 1] == 0x44 &&
                                currentBuffer[i + 2] == 0x52 && currentBuffer[i + 3] == 0x4B &&
                                currentBuffer[i + 4] == 0x30 && currentBuffer[i + 5] == 0x30 &&
                                currentBuffer[i + 6] == 0x30 && currentBuffer[i + 7] == 0x30)
                            {
                                int overlapSize = currentBuffer.Length - i;
                                overlapBuffer = new byte[overlapSize];
                                Array.Copy(currentBuffer, i, overlapBuffer, 0, overlapSize);
                                lastBufferHadPartialMatch = true;
                                break;
                            }
                        }
                    }
                }
            }

            return extractedCount;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
        }
    }
}