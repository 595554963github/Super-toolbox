namespace super_toolbox
{
    public class MusouOrochi2Ultimate_Extractor : BaseExtractor
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

            var idxFiles = Directory.EnumerateFiles(directoryPath, "linkdata.idx", SearchOption.AllDirectories).ToList();

            if (idxFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"未找到linkdata.idx文件");
                OnExtractionFailed($"未找到linkdata.idx文件");
                return;
            }

            TotalFilesToExtract = idxFiles.Count;
            ExtractionProgress?.Invoke(this, $"找到{idxFiles.Count}个idx文件,开始提取...");

            int processedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                foreach (var idxPath in idxFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processedCount++;

                    string fileName = Path.GetFileNameWithoutExtension(idxPath);
                    string dirPath = Path.GetDirectoryName(idxPath) ?? string.Empty;
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{idxFiles.Count}): {fileName}");

                    try
                    {
                        string binPath = Path.Combine(dirPath, "linkdata.bin");
                        int extractedCount = await ExtractIdxBinPair(idxPath, binPath, cancellationToken);
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

                ExtractionProgress?.Invoke(this, $"所有linkdata文件处理完成,总共提取{totalExtractedFiles}个文件");
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

        private async Task<int> ExtractIdxBinPair(string idxPath, string binPath, CancellationToken cancellationToken)
        {
            if (!File.Exists(idxPath))
            {
                ExtractionError?.Invoke(this, $"索引文件不存在{idxPath}");
                return 0;
            }

            if (!File.Exists(binPath))
            {
                ExtractionError?.Invoke(this, $"数据文件不存在{binPath}");
                return 0;
            }

            string outputDir = Path.Combine(Path.GetDirectoryName(idxPath) ?? Environment.CurrentDirectory, "linkdata");

            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
                await Task.Delay(300, cancellationToken);
            }

            Directory.CreateDirectory(outputDir);
            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(idxPath)}/.bin");
            ExtractionProgress?.Invoke(this, $"输出到:{outputDir}");

            int extractedCount = 0;

            try
            {
                using (var idxFs = new FileStream(idxPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var idxReader = new BinaryReader(idxFs))
                using (var binFs = new FileStream(binPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var binReader = new BinaryReader(binFs))
                {
                    idxFs.Seek(0xC, SeekOrigin.Begin);
                    uint tmp = idxReader.ReadUInt32();
                    bool bigEndian = tmp != 0;

                    idxFs.Seek(0, SeekOrigin.Begin);

                    long binSize = new FileInfo(binPath).Length;
                    int fileIndex = 0;

                    while (idxFs.Position + 32 <= idxFs.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        long offset = ReadLong(idxReader, bigEndian);
                        long size = ReadLong(idxReader, bigEndian);
                        long zsize = ReadLong(idxReader, bigEndian);
                        long zip = ReadLong(idxReader, bigEndian);

                        if (size == 0)
                        {
                            fileIndex++;
                            continue;
                        }

                        if (offset >= binSize || offset < 0)
                        {
                            fileIndex++;
                            continue;
                        }

                        string extension = "dat";
                        if (offset + 4 <= binSize)
                        {
                            binFs.Seek(offset, SeekOrigin.Begin);
                            byte[] header = binReader.ReadBytes(4);
                            extension = GetExtensionFromHeader(header);
                        }

                        string outputFileName = $"LINKDATA_{fileIndex:00000}.{extension}";
                        string outputFilePath = Path.Combine(outputDir, outputFileName);

                        binFs.Seek(offset, SeekOrigin.Begin);

                        long actualSize = zip != 0 ? zsize : size;

                        if (offset + actualSize > binSize)
                        {
                            actualSize = binSize - offset;
                        }

                        if (actualSize <= 0)
                        {
                            fileIndex++;
                            continue;
                        }

                        byte[] fileData = new byte[actualSize];
                        int bytesRead = await binFs.ReadAsync(fileData, 0, (int)actualSize, cancellationToken);

                        if (bytesRead > 0)
                        {
                            byte[] finalData = new byte[bytesRead];
                            Array.Copy(fileData, 0, finalData, 0, bytesRead);
                            await File.WriteAllBytesAsync(outputFilePath, finalData, cancellationToken);

                            extractedCount++;
                            double sizeKB = bytesRead / 1024.0;
                            ExtractionProgress?.Invoke(this, $"{outputFileName,-40}- 偏移:{offset:X8} 大小:{bytesRead,9}字节({sizeKB,7:F1}KB)");
                            OnFileExtracted(outputFilePath);
                        }

                        fileIndex++;
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
                ExtractionError?.Invoke(this, $"提取idx/bin文件时出错:{ex.Message}");
                throw new Exception($"提取idx/bin文件时出错:{ex.Message}", ex);
            }
        }

        private string GetExtensionFromHeader(byte[] header)
        {
            if (header.Length < 4) return "dat";

            if (header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01 && header[3] == 0x00)
                return "zlib";

            if (header[0] == 0x47 && header[1] == 0x54 && header[2] == 0x31 && header[3] == 0x47)
                return "g1t";

            if (header[0] == 0x5F && header[1] == 0x4D && header[2] == 0x31 && header[3] == 0x47)
                return "g1m";

            if (header[0] == 0x5F && header[1] == 0x4C && header[2] == 0x31 && header[3] == 0x47)
                return "g1l";

            if (header[0] == 0x4B && header[1] == 0x54 && header[2] == 0x41 && header[3] == 0x43)
                return "ktac";

            return "dat";
        }

        private long ReadLong(BinaryReader reader, bool bigEndian)
        {
            byte[] bytes = reader.ReadBytes(8);
            if (bigEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToInt64(bytes, 0);
        }
    }
}