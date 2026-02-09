namespace super_toolbox
{
    public class GustGspk_Extractor : BaseExtractor
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

            var trdFiles = Directory.GetFiles(directoryPath, "*.trd", SearchOption.AllDirectories).ToList();
            var gspkFiles = Directory.GetFiles(directoryPath, "*.gspk", SearchOption.AllDirectories).ToList();
            var allFiles = gspkFiles.Concat(trdFiles).ToList();

            if (allFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到.gspk或.trd文件");
                OnExtractionFailed("未找到.gspk或.trd文件");
                return;
            }

            TotalFilesToExtract = allFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{allFiles.Count}个文件(.gspk:{gspkFiles.Count}, .trd:{trdFiles.Count})");

            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    int totalExtractedFiles = 0;

                    foreach (var filePath in allFiles)
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

                            string fileExt = Path.GetExtension(filePath).ToLower();
                            bool isGspkFile = fileExt == ".gspk";

                            if (isGspkFile)
                            {
                                byte[] expectedHeader = new byte[] { 0x47, 0x53, 0x50, 0x4B, 0x31, 0x2E, 0x30, 0x00 };
                                if (fileData.Length < 8 || !fileData.Take(8).SequenceEqual(expectedHeader))
                                {
                                    ExtractionError?.Invoke(this, $"文件{fileName}:无效的GSPK文件头");
                                    continue;
                                }
                            }

                            const int START_OFFSET = 0x30;
                            List<uint> fileOffsets = new List<uint>();

                            for (int offsetIndex = START_OFFSET; offsetIndex < fileData.Length; offsetIndex += 4)
                            {
                                if (cancellationToken.IsCancellationRequested) break;
                                if (offsetIndex + 4 > fileData.Length) break;

                                bool isFourZero = true;
                                for (int i = 0; i < 4; i++)
                                {
                                    if (fileData[offsetIndex + i] != 0x00)
                                    {
                                        isFourZero = false;
                                        break;
                                    }
                                }
                                if (isFourZero) break;

                                uint fileOffset = BitConverter.ToUInt32(fileData, offsetIndex);
                                if (fileOffset != 0 && fileOffset < fileData.Length)
                                {
                                    fileOffsets.Add(fileOffset);
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
                                if (currentOffset + 4 <= fileData.Length)
                                {
                                    byte[] header = new byte[4];
                                    Array.Copy(fileData, (int)currentOffset, header, 0, 4);

                                    byte[] s2gHeader = new byte[] { 0x5F, 0x53, 0x32, 0x47 };
                                    byte[] kftkHeader = new byte[] { 0x4B, 0x46, 0x54, 0x4B };

                                    if (header.SequenceEqual(s2gHeader))
                                    {
                                        extension = ".g2s";
                                    }
                                    else if (header.SequenceEqual(kftkHeader))
                                    {
                                        extension = ".ktf";
                                    }
                                }

                                string outputFileName = $"{baseName}_{extractedCount + 1}{extension}";
                                string outputPath = Path.Combine(parentDir, outputFileName);
                                byte[] outputData = new byte[fileSize];
                                Array.Copy(fileData, (int)currentOffset, outputData, 0, fileSize);
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