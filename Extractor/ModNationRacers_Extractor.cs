namespace super_toolbox
{
    public class ModNationRacers_Extractor : BaseExtractor
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
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var hdrFiles = Directory.EnumerateFiles(directoryPath, "*.hdr", SearchOption.AllDirectories).ToList();

            if (hdrFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"未找到.hdr文件");
                OnExtractionFailed($"未找到.hdr文件");
                return;
            }

            TotalFilesToExtract = hdrFiles.Count;
            ExtractionProgress?.Invoke(this, $"找到{hdrFiles.Count}个.hdr文件,开始提取...");

            int processedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                foreach (var hdrPath in hdrFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processedCount++;

                    string fileName = Path.GetFileNameWithoutExtension(hdrPath);
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{hdrFiles.Count}): {fileName}");

                    try
                    {
                        string datPath = Path.Combine(Path.GetDirectoryName(hdrPath) ?? string.Empty, fileName + ".dat");
                        int extractedCount = await ExtractHdrDatPair(hdrPath, datPath, cancellationToken);
                        totalExtractedFiles += extractedCount;
                        ExtractionProgress?.Invoke(this, $"{fileName}提取完成,共提取{extractedCount}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{fileName}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"所有.hdr/.dat文件处理完成,总共提取{totalExtractedFiles}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程中出错:{ex.Message}");
                OnExtractionFailed($"提取过程中出错:{ex.Message}");
            }
        }

        private async Task<int> ExtractHdrDatPair(string hdrPath, string datPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(hdrPath))
            {
                ExtractionError?.Invoke(this, $"头文件不存在{hdrPath}");
                return 0;
            }

            if (!File.Exists(datPath))
            {
                ExtractionError?.Invoke(this, $"数据文件不存在{datPath}");
                return 0;
            }

            string basename = Path.GetFileNameWithoutExtension(hdrPath);
            string outputDir = Path.Combine(Path.GetDirectoryName(hdrPath) ?? Environment.CurrentDirectory, basename);

            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
                await Task.Delay(300, cancellationToken);
            }

            Directory.CreateDirectory(outputDir);
            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(hdrPath)}/.dat");
            ExtractionProgress?.Invoke(this, $"输出到:{outputDir}");

            int extractedCount = 0;
            long datFileSize = new FileInfo(datPath).Length;

            try
            {
                using (var hdrFs = new FileStream(hdrPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var hdrReader = new BinaryReader(hdrFs))
                using (var datFs = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var datReader = new BinaryReader(datFs))
                {
                    int entryIndex = 0;
                    long currentOffset = 0x2C;
                    long nextValidOffset = 0;
                    HashSet<long> extractedOffsets = new HashSet<long>();

                    while (currentOffset + 8 <= hdrFs.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        hdrFs.Seek(currentOffset, SeekOrigin.Begin);
                        uint fileSize = hdrReader.ReadUInt32();
                        uint fileOffset = hdrReader.ReadUInt32();

                        if (fileSize == 0 || fileOffset == 0)
                        {
                            currentOffset += 32;
                            entryIndex++;
                            continue;
                        }

                        if (extractedOffsets.Contains(fileOffset))
                        {
                            ExtractionProgress?.Invoke(this, $"条目{entryIndex}:偏移{fileOffset:X8}已提取过，跳过");
                            currentOffset += 32;
                            entryIndex++;
                            continue;
                        }

                        if (fileOffset >= datFileSize)
                        {
                            ExtractionProgress?.Invoke(this, $"条目{entryIndex}:偏移地址{fileOffset:X8}超出dat文件范围");
                            currentOffset += 32;
                            entryIndex++;
                            continue;
                        }

                        if (fileOffset + fileSize > datFileSize)
                        {
                            ExtractionProgress?.Invoke(this, $"条目{entryIndex}:偏移{fileOffset:X8}+大小{fileSize}超出dat文件范围");
                            currentOffset += 32;
                            entryIndex++;
                            continue;
                        }

                        if (fileOffset < nextValidOffset)
                        {
                            ExtractionProgress?.Invoke(this, $"条目{entryIndex}:偏移{fileOffset:X8}小于预期偏移{nextValidOffset:X8}，可能已提取过");
                            currentOffset += 32;
                            entryIndex++;
                            continue;
                        }

                        datFs.Seek(fileOffset, SeekOrigin.Begin);
                        byte[] header = datReader.ReadBytes(4);
                        if (header.Length < 4)
                        {
                            currentOffset += 32;
                            entryIndex++;
                            continue;
                        }

                        string extension = DetermineExtension(header);
                        string outputFileName = $"{basename}_{entryIndex:000}.{extension}";
                        string outputFilePath = Path.Combine(outputDir, outputFileName);

                        datFs.Seek(fileOffset, SeekOrigin.Begin);
                        byte[] fileData = datReader.ReadBytes((int)fileSize);

                        if (fileData.Length == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"条目{entryIndex}:数据长度为0");
                            currentOffset += 32;
                            entryIndex++;
                            continue;
                        }

                        await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);

                        extractedCount++;
                        extractedOffsets.Add(fileOffset);
                        nextValidOffset = fileOffset + fileSize;
                        double sizeKB = fileSize / 1024.0;

                        ExtractionProgress?.Invoke(this, $"{outputFileName,-40}- 偏移:{fileOffset:X8} 大小:{fileSize,9}字节({sizeKB,7:F1}KB)");
                        OnFileExtracted(outputFilePath);

                        currentOffset += 32;
                        entryIndex++;

                        if (nextValidOffset >= datFileSize)
                        {
                            ExtractionProgress?.Invoke(this, $"dat文件已完全提取，停止处理剩余条目");
                            break;
                        }
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成!提取了{extractedCount}个文件到'{outputDir}'目录");

                if (extractedCount == 0)
                {
                    ExtractionError?.Invoke(this, $"警告:没有提取到任何文件!可能原因:文件格式不正确或已损坏");
                }

                return extractedCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取.hdr/.dat文件时出错:{ex.Message}");
                throw new Exception($"提取.hdr/.dat文件时出错:{ex.Message}", ex);
            }
        }

        private string DetermineExtension(byte[] header)
        {
            if (header.Length < 4) return "bin";

            string magic = System.Text.Encoding.ASCII.GetString(header, 0, 4);

            return magic switch
            {
                "GXP " => "gxp",
                "RIFF" => "at9",
                "BKHD" => "bnk",
                "AKPK" => "pck",
                _ => "bin"
            };
        }
    }
}
