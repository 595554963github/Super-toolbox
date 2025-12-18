namespace super_toolbox
{
    public class SonyGxtExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] GXT_HEADER = { 0x47, 0x58, 0x54, 0x00, 0x03, 0x00, 0x00, 0x10 };
        private const int BUFFER_SIZE = 8192;
        private const int MAX_SEARCH_SIZE = 50 * 1024 * 1024;

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
        }
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
            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase) &&
                              !Path.GetExtension(file).Equals(".gxt", StringComparison.OrdinalIgnoreCase))
                .ToList();
            int sourceFileCount = filePaths.Count;
            TotalFilesToExtract = 0;
            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, FileOptions.Asynchronous);
                    string filePrefix = Path.GetFileNameWithoutExtension(filePath);
                    long position = 0;
                    byte[] buffer = new byte[BUFFER_SIZE];
                    byte[] leftover = Array.Empty<byte>();

                    while (position < fileStream.Length)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        int bytesToRead = (int)Math.Min(BUFFER_SIZE, fileStream.Length - position);
                        int bytesRead = await fileStream.ReadAsync(buffer, 0, bytesToRead, cancellationToken);
                        if (bytesRead == 0) break;

                        byte[] currentData;
                        if (leftover.Length > 0)
                        {
                            currentData = new byte[leftover.Length + bytesRead];
                            Array.Copy(leftover, 0, currentData, 0, leftover.Length);
                            Array.Copy(buffer, 0, currentData, leftover.Length, bytesRead);
                        }
                        else
                        {
                            currentData = new byte[bytesRead];
                            Array.Copy(buffer, 0, currentData, 0, bytesRead);
                        }

                        int headerPos = IndexOf(currentData, GXT_HEADER, 0);
                        if (headerPos != -1)
                        {
                            long headerFilePos = position - leftover.Length + headerPos;
                            long gxtSize = await DetermineGxtSizeAsync(fileStream, headerFilePos, cancellationToken);

                            if (gxtSize > 0)
                            {
                                await ExtractGxtSegmentAsync(fileStream, headerFilePos, gxtSize, filePrefix, extractedDir, extractedFiles);
                                position = headerFilePos + gxtSize;
                                fileStream.Seek(position, SeekOrigin.Begin);
                                leftover = Array.Empty<byte>();
                                continue;
                            }
                        }
                        leftover = currentData.Length > GXT_HEADER.Length
                            ? currentData[^GXT_HEADER.Length..]
                            : currentData;

                        position += bytesRead;
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (IOException e)
                {
                    ExtractionError?.Invoke(this, $"读取文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"读取文件{filePath}时出错:{e.Message}");
                }
            }
            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，从{sourceFileCount}个源文件中提取出{extractedFiles.Count}个GXT文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"处理完成，从{sourceFileCount}个源文件中未找到GXT文件");
            }
            OnExtractionCompleted();
        }
        private async Task<long> DetermineGxtSizeAsync(FileStream fileStream, long headerPosition, CancellationToken cancellationToken)
        {
            long fileSize = fileStream.Length;
            long searchEnd = Math.Min(headerPosition + MAX_SEARCH_SIZE, fileSize);

            byte[] buffer = new byte[BUFFER_SIZE];
            byte[] leftover = Array.Empty<byte>();
            long currentPos = headerPosition;

            fileStream.Seek(headerPosition, SeekOrigin.Begin);

            while (currentPos < searchEnd)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int bytesToRead = (int)Math.Min(BUFFER_SIZE, searchEnd - currentPos);
                int bytesRead = await fileStream.ReadAsync(buffer, 0, bytesToRead, cancellationToken);
                if (bytesRead == 0) break;

                byte[] currentData;
                if (leftover.Length > 0)
                {
                    currentData = new byte[leftover.Length + bytesRead];
                    Array.Copy(leftover, 0, currentData, 0, leftover.Length);
                    Array.Copy(buffer, 0, currentData, leftover.Length, bytesRead);
                }
                else
                {
                    currentData = new byte[bytesRead];
                    Array.Copy(buffer, 0, currentData, 0, bytesRead);
                }

                int nextHeaderPos = IndexOf(currentData, GXT_HEADER, 0);
                if (nextHeaderPos != -1 && (currentPos - leftover.Length + nextHeaderPos) > headerPosition)
                {
                    return (currentPos - leftover.Length + nextHeaderPos) - headerPosition;
                }

                leftover = currentData.Length > GXT_HEADER.Length
                    ? currentData[^GXT_HEADER.Length..]
                    : currentData;

                currentPos += bytesRead;
            }
            return fileSize - headerPosition;
        }
        private async Task ExtractGxtSegmentAsync(FileStream sourceStream, long startPosition, long length,
                                                string filePrefix, string extractedDir, List<string> extractedFiles)
        {
            if (length <= 0) return;

            string outputFileName = $"{filePrefix}_{extractedFiles.Count + 1}.gxt";
            string outputFilePath = Path.Combine(extractedDir, outputFileName);

            if (File.Exists(outputFilePath))
            {
                int duplicateCount = 1;
                do
                {
                    outputFileName = $"{filePrefix}_{extractedFiles.Count + 1}_dup{duplicateCount}.gxt";
                    outputFilePath = Path.Combine(extractedDir, outputFileName);
                    duplicateCount++;
                } while (File.Exists(outputFilePath));
            }
            try
            {
                await using var outputStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_SIZE, FileOptions.Asynchronous);
                sourceStream.Seek(startPosition, SeekOrigin.Begin);

                byte[] buffer = new byte[BUFFER_SIZE];
                long bytesRemaining = length;
                long totalBytesWritten = 0;

                while (bytesRemaining > 0)
                {
                    int bytesToRead = (int)Math.Min(bytesRemaining, BUFFER_SIZE);
                    int bytesRead = await sourceStream.ReadAsync(buffer, 0, bytesToRead);
                    if (bytesRead == 0) break;

                    await outputStream.WriteAsync(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                    totalBytesWritten += bytesRead;
                }

                if (totalBytesWritten == length)
                {
                    if (!extractedFiles.Contains(outputFilePath))
                    {
                        extractedFiles.Add(outputFilePath);
                        OnFileExtracted(outputFilePath);
                        ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
                    }
                }
                else
                {
                    ExtractionError?.Invoke(this, $"文件{outputFileName}提取不完整(预期:{length}字节, 实际:{totalBytesWritten}字节)");
                    OnExtractionFailed($"文件{outputFileName}提取不完整(预期:{length} 字节, 实际:{totalBytesWritten}字节)");
                    try { File.Delete(outputFilePath); } catch { }
                }
            }
            catch (IOException e)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                OnExtractionFailed($"写入文件{outputFilePath}时出错:{e.Message}");
            }
        }
        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("提取操作已取消", cancellationToken);
            }
        }
    }
}
