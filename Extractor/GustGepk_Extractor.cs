namespace super_toolbox
{
    public class GustGepk_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var gepkFiles = Directory.GetFiles(directoryPath, "*.gepk", SearchOption.AllDirectories).ToList();

            if (gepkFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到.gepk文件");
                OnExtractionFailed("未找到.gepk文件");
                return;
            }

            TotalFilesToExtract = gepkFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{gepkFiles.Count}个.gepk文件");

            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    int totalExtractedFiles = 0;

                    foreach (var filePath in gepkFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedCount++;
                        string fileName = Path.GetFileName(filePath);

                        try
                        {
                            string? parentDir = Path.GetDirectoryName(filePath);
                            if (string.IsNullOrEmpty(parentDir))
                            {
                                ExtractionError?.Invoke(this, $"无法获取文件目录:{filePath}");
                                continue;
                            }

                            byte[] fileData = File.ReadAllBytes(filePath);
                            string baseName = Path.GetFileNameWithoutExtension(filePath);
                            int extractedCount = 0;

                            byte[] expectedHeader = new byte[] { 0x47, 0x45, 0x50, 0x4B, 0x31, 0x2E, 0x32, 0x00 };
                            if (fileData.Length < 8 || !fileData.Take(8).SequenceEqual(expectedHeader))
                            {
                                ExtractionError?.Invoke(this, $"文件{fileName}:无效的GEPK文件头");
                                continue;
                            }

                            const int START_OFFSET = 0x24;
                            const int INDEX_END_OFFSET = 0x90;

                            if (START_OFFSET + 4 > fileData.Length)
                            {
                                ExtractionError?.Invoke(this, $"文件{fileName}:文件长度不足");
                                continue;
                            }

                            List<uint> fileOffsets = new List<uint>();

                            for (int offsetIndex = START_OFFSET; offsetIndex < INDEX_END_OFFSET; offsetIndex += 4)
                            {
                                if (cancellationToken.IsCancellationRequested) break;
                                if (offsetIndex + 4 > fileData.Length) break;

                                uint offsetValue = BitConverter.ToUInt32(fileData, offsetIndex);

                                if (offsetValue == 0x00000000) continue;

                                if (offsetValue >= fileData.Length) continue;

                                if (offsetValue > 0 && offsetValue < fileData.Length)
                                {
                                    fileOffsets.Add(offsetValue);
                                }
                            }

                            if (fileOffsets.Count == 0)
                            {
                                ExtractionError?.Invoke(this, $"文件{fileName}:未找到有效的文件偏移地址");
                                continue;
                            }

                            uint firstFileOffset = fileOffsets[0];

                            for (int offsetIndex = START_OFFSET; offsetIndex < firstFileOffset; offsetIndex += 4)
                            {
                                if (cancellationToken.IsCancellationRequested) break;
                                if (offsetIndex + 4 > firstFileOffset) break;

                                uint offsetValue = BitConverter.ToUInt32(fileData, offsetIndex);

                                if (offsetValue == 0x00000000) continue;

                                if (offsetValue > 0 && offsetValue < firstFileOffset)
                                {
                                    if (!fileOffsets.Contains(offsetValue))
                                    {
                                        fileOffsets.Add(offsetValue);
                                    }
                                }
                            }

                            fileOffsets = fileOffsets.Distinct().OrderBy(o => o).ToList();

                            for (int i = 0; i < fileOffsets.Count; i++)
                            {
                                if (cancellationToken.IsCancellationRequested) break;

                                uint currentOffset = fileOffsets[i];
                                uint fileSize = 0;

                                if (i < fileOffsets.Count - 1)
                                {
                                    uint nextOffset = fileOffsets[i + 1];
                                    if (nextOffset > currentOffset)
                                    {
                                        fileSize = nextOffset - currentOffset;
                                    }
                                }
                                else
                                {
                                    fileSize = (uint)(fileData.Length - currentOffset);
                                }

                                if (fileSize == 0 || currentOffset + fileSize > fileData.Length) continue;

                                string extension = ".bin";
                                uint actualFileSize = fileSize;

                                if (currentOffset + 4 <= fileData.Length)
                                {
                                    byte[] header = new byte[4];
                                    Array.Copy(fileData, (int)currentOffset, header, 0, 4);

                                    byte[] me1gHeader = new byte[] { 0x4D, 0x45, 0x31, 0x47 };
                                    byte[] xf1gHeader = new byte[] { 0x58, 0x46, 0x31, 0x47 };
                                    byte[] gt1gHeader = new byte[] { 0x47, 0x54, 0x31, 0x47 };

                                    if (header.SequenceEqual(me1gHeader))
                                    {
                                        extension = ".g1e";
                                    }
                                    else if (header.SequenceEqual(xf1gHeader))
                                    {
                                        extension = ".g1f";
                                    }
                                    else if (header.SequenceEqual(gt1gHeader))
                                    {
                                        extension = ".g1t";
                                        if (currentOffset + 0x0C <= fileData.Length)
                                        {
                                            uint sizeFromHeader = BitConverter.ToUInt32(fileData, (int)currentOffset + 0x08);
                                            if (sizeFromHeader > 0 && sizeFromHeader <= fileSize)
                                            {
                                                actualFileSize = sizeFromHeader;
                                            }
                                        }
                                    }
                                }
                                if (actualFileSize == 0 || currentOffset + actualFileSize > fileData.Length) continue;

                                string outputFileName = $"{baseName}_{extractedCount + 1}{extension}";
                                string outputPath = Path.Combine(parentDir, outputFileName);
                                byte[] outputData = new byte[actualFileSize];
                                Array.Copy(fileData, (int)currentOffset, outputData, 0, actualFileSize);
                                File.WriteAllBytes(outputPath, outputData);
                                extractedCount++;
                                totalExtractedFiles++;
                                OnFileExtracted(outputPath);
                            }

                            ExtractionProgress?.Invoke(this, $"文件{fileName}提取完成,找到{extractedCount}个文件");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{fileName}处理错误:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"提取完成,总共生成{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}