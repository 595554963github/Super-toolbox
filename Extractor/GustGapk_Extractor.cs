namespace super_toolbox
{
    public class GustGapk_Extractor : BaseExtractor
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
            var gapkFiles = Directory.GetFiles(directoryPath, "*.gapk", SearchOption.AllDirectories);
            var campkFiles = Directory.GetFiles(directoryPath, "*.campk", SearchOption.AllDirectories);
            var allFiles = gapkFiles.Concat(campkFiles).ToList();

            if (allFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到.gapk或.campk文件");
                OnExtractionFailed("未找到.gapk或.campk文件");
                return;
            }

            TotalFilesToExtract = allFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{allFiles.Count}个文件(.gapk:{gapkFiles.Length}, .campk:{campkFiles.Length})");

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
                        if (processedCount % 10 == 1 || processedCount == allFiles.Count)
                        {
                            ExtractionProgress?.Invoke(this, $"正在处理:{fileName} ({processedCount}/{allFiles.Count})");
                        }

                        try
                        {
                            string? parentDir = Path.GetDirectoryName(filePath);
                            if (string.IsNullOrEmpty(parentDir))
                            {
                                ExtractionError?.Invoke(this, $"无法获取文件目录:{filePath}");
                                continue;
                            }

                            string fileExt = Path.GetExtension(filePath).ToLower();
                            string outputSubDir = fileExt == ".gapk" ? "gapk" : "campk";
                            string outputRootDir = Path.Combine(parentDir, outputSubDir);
                            if (!Directory.Exists(outputRootDir))
                            {
                                Directory.CreateDirectory(outputRootDir);
                            }

                            byte[] fileData = File.ReadAllBytes(filePath);
                            string baseName = Path.GetFileNameWithoutExtension(filePath);
                            int extractedCount = 0;

                            uint indexStartOffset = fileExt == ".gapk" ? 0x24u : 0x30u;
                            if (indexStartOffset + 4 > fileData.Length)
                            {
                                ExtractionError?.Invoke(this, $"文件{fileName}:文件长度不足,无法解析偏移地址");
                                continue;
                            }

                            uint indexDataBoundary = BitConverter.ToUInt32(fileData, (int)indexStartOffset);
                            if (indexDataBoundary == 0 || indexDataBoundary >= fileData.Length)
                            {
                                ExtractionError?.Invoke(this, $"文件{fileName}:无效的索引数据区边界值");
                                continue;
                            }

                            List<uint> fileOffsets = new List<uint>();
                            bool stopExtraction = false;

                            for (int offsetIndex = (int)indexStartOffset; offsetIndex < indexDataBoundary; offsetIndex += 4)
                            {
                                if (cancellationToken.IsCancellationRequested) break;

                                if (offsetIndex + 4 > fileData.Length) break;

                                if (fileExt == ".campk")
                                {
                                    bool isFourZero = true;
                                    for (int i = 0; i < 4; i++)
                                    {
                                        if (fileData[offsetIndex + i] != 0x00)
                                        {
                                            isFourZero = false;
                                            break;
                                        }
                                    }
                                    if (isFourZero)
                                    {
                                        stopExtraction = true;
                                        break;
                                    }
                                }

                                uint fileOffset = BitConverter.ToUInt32(fileData, offsetIndex);
                                if (fileOffset != 0 && fileOffset >= indexDataBoundary && fileOffset < fileData.Length)
                                {
                                    fileOffsets.Add(fileOffset);
                                }
                            }

                            if (stopExtraction)
                            {
                                ExtractionProgress?.Invoke(this, $"文件{fileName}:遇到连续4个00,中止提取");
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
                                byte[] g1aHeader = new byte[] { 0x5F, 0x41, 0x31, 0x47 };
                                if (currentOffset + 4 <= fileData.Length)
                                {
                                    byte[] header = new byte[4];
                                    Array.Copy(fileData, (int)currentOffset, header, 0, 4);
                                    if (header.SequenceEqual(g1aHeader))
                                    {
                                        extension = ".g1a";
                                    }
                                }

                                string outputFileName = $"{baseName}_{extractedCount + 1}{extension}";
                                string outputPath = Path.Combine(outputRootDir, outputFileName);
                                byte[] outputData = new byte[fileSize];
                                Array.Copy(fileData, (int)currentOffset, outputData, 0, fileSize);
                                File.WriteAllBytes(outputPath, outputData);
                                extractedCount++;
                                totalExtractedFiles++;
                                OnFileExtracted(outputPath);
                            }

                            ExtractionProgress?.Invoke(this, $"文件{fileName}提取完成,找到{extractedCount}个文件,保存至{outputSubDir}文件夹");
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