namespace super_toolbox
{
    public class Bnsf_Extractor : BaseExtractor
    {
        private static readonly byte[] BNSF_HEADER = { 0x42, 0x4E, 0x53, 0x46 }; // 'BNSF'
        private static readonly byte[] SFMT_MARKER = { 0x73, 0x66, 0x6D, 0x74 };  // 'sfmt'
        private const int BUFFER_SIZE = 81920;
        private const int SFMT_OFFSET = 12;
        private const int MIN_BNSF_SIZE = 16;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath} 不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath} 不是有效的目录");
                return;
            }

            string extractedRootDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedRootDir);

            var tldatFiles = Directory.GetFiles(directoryPath, "*.TLDAT", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedRootDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = tldatFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{tldatFiles.Count}个TLDAT文件");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var tldatFilePath in tldatFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(tldatFilePath)}");

                            int extractedCount = ProcessFileByChunks(tldatFilePath, extractedRootDir, cancellationToken);

                            if (extractedCount > 0)
                            {
                                ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(tldatFilePath)}中提取出{extractedCount}个BNSF文件");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(tldatFilePath)}时出错:{ex.Message}");
                            OnExtractionFailed($"处理{Path.GetFileName(tldatFilePath)}时出错:{ex.Message}");
                        }
                    }
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        private int ProcessFileByChunks(string filePath, string outputDir, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            byte[] buffer = new byte[BUFFER_SIZE];
            byte[] overlapBuffer = new byte[BNSF_HEADER.Length - 1];
            int overlapSize = 0;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, FileOptions.SequentialScan))
            {
                int bytesRead;
                while ((bytesRead = fs.Read(buffer, overlapSize, BUFFER_SIZE - overlapSize)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int totalBytes = bytesRead + overlapSize;
                    for (int i = 0; i < totalBytes - 15; i++)
                    {
                        if (buffer[i] == BNSF_HEADER[0] &&
                            buffer[i + 1] == BNSF_HEADER[1] &&
                            buffer[i + 2] == BNSF_HEADER[2] &&
                            buffer[i + 3] == BNSF_HEADER[3])
                        {
                            if (i + SFMT_OFFSET + 4 <= totalBytes &&
                                buffer[i + SFMT_OFFSET] == SFMT_MARKER[0] &&
                                buffer[i + SFMT_OFFSET + 1] == SFMT_MARKER[1] &&
                                buffer[i + SFMT_OFFSET + 2] == SFMT_MARKER[2] &&
                                buffer[i + SFMT_OFFSET + 3] == SFMT_MARKER[3])
                            {
                                long startPos = fs.Position - totalBytes + i;
                                long endPos = FindNextHeader(fs, startPos + 4);

                                if (endPos - startPos >= MIN_BNSF_SIZE)
                                {
                                    SaveBNSFChunk(fs, outputDir, Path.GetFileNameWithoutExtension(filePath),
                                        startPos, endPos, extractedCount + 1);
                                    extractedCount++;
                                    i = (int)(endPos - (fs.Position - totalBytes)) - 1;
                                }
                            }
                        }
                    }

                    overlapSize = Math.Min(BNSF_HEADER.Length - 1, totalBytes);
                    Array.Copy(buffer, totalBytes - overlapSize, overlapBuffer, 0, overlapSize);
                    Array.Copy(overlapBuffer, buffer, overlapSize);
                }
            }

            return extractedCount;
        }

        private long FindNextHeader(FileStream fs, long startSearchPos)
        {
            byte[] searchBuffer = new byte[BUFFER_SIZE];
            fs.Seek(startSearchPos, SeekOrigin.Begin);

            while (fs.Position < fs.Length)
            {
                int bytesRead = fs.Read(searchBuffer, 0, BUFFER_SIZE);
                for (int i = 0; i < bytesRead - 3; i++)
                {
                    if (searchBuffer[i] == BNSF_HEADER[0] &&
                        searchBuffer[i + 1] == BNSF_HEADER[1] &&
                        searchBuffer[i + 2] == BNSF_HEADER[2] &&
                        searchBuffer[i + 3] == BNSF_HEADER[3])
                    {
                        return fs.Position - bytesRead + i;
                    }
                }
            }
            return fs.Length;
        }

        private void SaveBNSFChunk(FileStream sourceFs, string outputDir,
            string baseName, long startPos, long endPos, int fileNumber)
        {
            string outputPath = Path.Combine(outputDir, $"{baseName}_{fileNumber}.bnsf");
            outputPath = GetUniqueFilePath(outputPath);

            try
            {
                using (var outputFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    sourceFs.Seek(startPos, SeekOrigin.Begin);
                    byte[] copyBuffer = new byte[8192];
                    long bytesRemaining = endPos - startPos;

                    while (bytesRemaining > 0)
                    {
                        int bytesToCopy = (int)Math.Min(copyBuffer.Length, bytesRemaining);
                        int bytesRead = sourceFs.Read(copyBuffer, 0, bytesToCopy);
                        outputFs.Write(copyBuffer, 0, bytesRead);
                        bytesRemaining -= bytesRead;
                    }
                }
                OnFileExtracted(outputPath);
                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"保存失败{outputPath}: {ex.Message}");
            }
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return filePath;
            }

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);
            int duplicateCount = 1;
            string newFilePath;

            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_dup{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
