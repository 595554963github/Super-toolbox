using AuroraLib.Compression.Algorithms;

namespace super_toolbox
{
    public class SzsSarc_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private class FormatInfo
        {
            public byte[] Signature { get; set; } = Array.Empty<byte>();
            public string Extension { get; set; } = string.Empty;
            public int SizeOffset { get; set; } = 12;
            public int HeaderSize { get; set; } = 0;
            public bool IsFlimFormat { get; set; }
            public bool IsLittleEndian { get; set; } = false;
        }

        private static readonly byte[] Yaz0Magic = { 0x59, 0x61, 0x7A, 0x30 };
        private static readonly byte[] SarcMagic = { 0x53, 0x41, 0x52, 0x43 };
        private static readonly byte[] BYML_SIGNATURE = { 0x42, 0x59, 0x00, 0x02, 0x00, 0x00, 0x00, 0x10 };
        private static readonly byte[] AAMP_SIGNATURE = { 0x41, 0x41, 0x4D, 0x50 };

        private readonly FormatInfo[] Formats = new[]
        {
            new FormatInfo { Signature = new byte[] { 0x46, 0x4C, 0x41, 0x4E }, Extension = "bflan" },
            new FormatInfo { Signature = new byte[] { 0x46, 0x4C, 0x59, 0x54 }, Extension = "bflyt" },
            new FormatInfo { Signature = new byte[] { 0x46, 0x53, 0x48, 0x41 }, Extension = "bfsha" },
            new FormatInfo { Signature = new byte[] { 0x46, 0x4C, 0x49, 0x4D }, Extension = "bflim", IsFlimFormat = true },
            new FormatInfo { Signature = new byte[] { 0x46, 0x52, 0x45, 0x53 }, Extension = "bfres" },
            new FormatInfo { Signature = new byte[] { 0x46, 0x47, 0x52, 0x50 }, Extension = "bfrgp" },
            new FormatInfo { Signature = new byte[] { 0x46, 0x57, 0x53, 0x44 }, Extension = "bfwsd" },
            new FormatInfo { Signature = new byte[] { 0x46, 0x57, 0x41, 0x52 }, Extension = "bfwar" },
            new FormatInfo { Signature = new byte[] { 0x46, 0x57, 0x41, 0x56 }, Extension = "bfwav" },
        };

        private uint ReadBigEndianUInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (uint)(
                data[offset] << 24 |
                data[offset + 1] << 16 |
                data[offset + 2] << 8 |
                data[offset + 3]
            );
        }

        private uint ReadLittleEndianUInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (uint)(
                data[offset] |
                data[offset + 1] << 8 |
                data[offset + 2] << 16 |
                data[offset + 3] << 24
            );
        }

        private int CalculateBymlSize(byte[] data, int startIndex)
        {
            if (startIndex + 16 > data.Length) return 0;

            uint nameArrayOffset = ReadBigEndianUInt32(data, startIndex + 4);
            uint stringArrayOffset = ReadBigEndianUInt32(data, startIndex + 8);
            uint rootNodeOffset = ReadBigEndianUInt32(data, startIndex + 12);

            uint maxOffset = rootNodeOffset;
            if (nameArrayOffset > maxOffset && nameArrayOffset < data.Length - startIndex)
                maxOffset = nameArrayOffset;
            if (stringArrayOffset > maxOffset && stringArrayOffset < data.Length - startIndex)
                maxOffset = stringArrayOffset;

            int estimatedSize = (int)maxOffset + 4096;

            for (int i = startIndex + (int)maxOffset; i < data.Length - 8; i++)
            {
                if (data[i] == BYML_SIGNATURE[0] && data[i + 1] == BYML_SIGNATURE[1] &&
                    data[i + 2] == BYML_SIGNATURE[2] && data[i + 3] == BYML_SIGNATURE[3] &&
                    data[i + 4] == BYML_SIGNATURE[4] && data[i + 5] == BYML_SIGNATURE[5] &&
                    data[i + 6] == BYML_SIGNATURE[6] && data[i + 7] == BYML_SIGNATURE[7])
                {
                    estimatedSize = i - startIndex;
                    break;
                }
            }

            if (estimatedSize > data.Length - startIndex)
                estimatedSize = data.Length - startIndex;

            return estimatedSize;
        }

        private byte[] DecompressYaz0(byte[] compressedData)
        {
            int yaz0Start = IndexOf(compressedData, Yaz0Magic, 0);
            if (yaz0Start < 0) return compressedData;

            using (var inputStream = new MemoryStream(compressedData))
            using (var outputStream = new MemoryStream())
            {
                inputStream.Seek(yaz0Start, SeekOrigin.Begin);
                Yaz0 yaz0 = new Yaz0();
                yaz0.Decompress(inputStream, outputStream);
                return outputStream.ToArray();
            }
        }

        private bool StartsWith(byte[] data, byte[] pattern)
        {
            if (data.Length < pattern.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
                if (data[i] != pattern[i]) return false;
            return true;
        }

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            if (data == null || pattern == null || startIndex < 0 || startIndex > data.Length - pattern.Length)
                return -1;

            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始扫描目录:{directoryPath}");

            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);

            if (allFiles.Length == 0)
            {
                ExtractionProgress?.Invoke(this, "目录中没有文件");
                OnExtractionCompleted();
                return;
            }

            TotalFilesToExtract = allFiles.Length;
            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;

                    foreach (var filePath in allFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedCount++;

                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)} ({processedCount}/{allFiles.Length})");

                        try
                        {
                            string? parentDir = Path.GetDirectoryName(filePath);
                            if (string.IsNullOrEmpty(parentDir)) continue;

                            string baseName = Path.GetFileNameWithoutExtension(filePath);
                            string outputDir = Path.Combine(parentDir, baseName);
                            Directory.CreateDirectory(outputDir);

                            byte[] fileData = File.ReadAllBytes(filePath);

                            if (StartsWith(fileData, Yaz0Magic))
                            {
                                ExtractionProgress?.Invoke(this, $"检测到Yaz0压缩,开始解压:{Path.GetFileName(filePath)}");
                                byte[] decompressedData = DecompressYaz0(fileData);

                                if (StartsWith(decompressedData, SarcMagic))
                                {
                                    int extractedCount = ExtractFromSarc(decompressedData, baseName, outputDir);
                                    totalExtractedFiles += extractedCount;
                                    ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(filePath)}解压并提取完成,找到{extractedCount}个文件");
                                }
                                else
                                {
                                    int extractedCount = ExtractEmbeddedFiles(decompressedData, baseName, outputDir);
                                    if (extractedCount > 0)
                                    {
                                        totalExtractedFiles += extractedCount;
                                        ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(filePath)}解压并提取完成,找到{extractedCount}个嵌入式文件");
                                    }
                                    else
                                    {
                                        string decompressedFilePath = Path.Combine(outputDir, $"{baseName}_decompressed.bin");
                                        File.WriteAllBytes(decompressedFilePath, decompressedData);
                                        totalExtractedFiles++;
                                        OnFileExtracted(decompressedFilePath);
                                        ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(filePath)}解压完成,保存为:{Path.GetFileName(decompressedFilePath)}");
                                    }
                                }
                            }
                            else if (StartsWith(fileData, SarcMagic))
                            {
                                int extractedCount = ExtractFromSarc(fileData, baseName, outputDir);
                                totalExtractedFiles += extractedCount;
                                ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(filePath)}提取完成,找到{extractedCount}个文件");
                            }
                            else
                            {
                                int extractedCount = ExtractEmbeddedFiles(fileData, baseName, outputDir);
                                if (extractedCount > 0)
                                {
                                    totalExtractedFiles += extractedCount;
                                    ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(filePath)}提取完成,找到{extractedCount}个嵌入式文件");
                                }
                                else
                                {
                                    totalExtractedFiles++;
                                    OnFileExtracted(filePath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(filePath)}处理错误:{ex.Message}");
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

        private int ExtractFromSarc(byte[] sarcData, string baseName, string outputDir)
        {
            int extractedCount = 0;
            var formatCounts = new Dictionary<string, int>();

            for (int i = 0; i < sarcData.Length - 4; i++)
            {
                foreach (var format in Formats)
                {
                    if (sarcData[i] == format.Signature[0] &&
                        sarcData[i + 1] == format.Signature[1] &&
                        sarcData[i + 2] == format.Signature[2] &&
                        sarcData[i + 3] == format.Signature[3])
                    {
                        if (i + format.SizeOffset + 4 > sarcData.Length) continue;

                        uint size = ReadBigEndianUInt32(sarcData, i + format.SizeOffset);

                        if (format.IsFlimFormat)
                        {
                            if (size < 40) continue;
                            int dataSize = (int)size - 40;
                            if (dataSize <= 0 || i - dataSize < 0) continue;

                            if (!formatCounts.ContainsKey(format.Extension))
                                formatCounts[format.Extension] = 0;
                            formatCounts[format.Extension]++;

                            string outputPath = Path.Combine(outputDir, $"{baseName}_{formatCounts[format.Extension]}.{format.Extension}");
                            outputPath = GetUniqueFilePath(outputPath);

                            byte[] flimData = new byte[size];
                            Array.Copy(sarcData, i - dataSize, flimData, 0, dataSize);
                            Array.Copy(sarcData, i, flimData, dataSize, 40);

                            File.WriteAllBytes(outputPath, flimData);
                            extractedCount++;
                            OnFileExtracted(outputPath);
                        }
                        else
                        {
                            if (size <= 0 || i + size > sarcData.Length) continue;

                            if (!formatCounts.ContainsKey(format.Extension))
                                formatCounts[format.Extension] = 0;
                            formatCounts[format.Extension]++;

                            string outputPath = Path.Combine(outputDir, $"{baseName}_{formatCounts[format.Extension]}.{format.Extension}");
                            outputPath = GetUniqueFilePath(outputPath);

                            byte[] outputData = new byte[size];
                            Array.Copy(sarcData, i, outputData, 0, size);
                            File.WriteAllBytes(outputPath, outputData);

                            extractedCount++;
                            OnFileExtracted(outputPath);
                            i += (int)size - 1;
                        }
                        break;
                    }
                }
            }

            int bymlCount = ExtractBymlFiles(sarcData, baseName, outputDir);
            int aampCount = ExtractAampFiles(sarcData, baseName, outputDir);

            return extractedCount + bymlCount + aampCount;
        }

        private int ExtractEmbeddedFiles(byte[] data, string baseName, string outputDir)
        {
            int bymlCount = ExtractBymlFiles(data, baseName, outputDir);
            int aampCount = ExtractAampFiles(data, baseName, outputDir);
            return bymlCount + aampCount;
        }

        private int ExtractBymlFiles(byte[] data, string baseName, string outputDir)
        {
            int index = 0;
            int fileIndex = 1;
            int extractedCount = 0;

            while (index <= data.Length - BYML_SIGNATURE.Length)
            {
                int startIndex = IndexOf(data, BYML_SIGNATURE, index);
                if (startIndex == -1) break;

                int fileSize = CalculateBymlSize(data, startIndex);
                if (fileSize <= 0 || startIndex + fileSize > data.Length)
                {
                    index = startIndex + 1;
                    continue;
                }

                byte[] fileData = new byte[fileSize];
                Array.Copy(data, startIndex, fileData, 0, fileSize);

                string fileName = $"{baseName}_{fileIndex}.byml";
                string outputPath = Path.Combine(outputDir, fileName);
                outputPath = GetUniqueFilePath(outputPath);

                File.WriteAllBytes(outputPath, fileData);
                extractedCount++;
                OnFileExtracted(outputPath);

                fileIndex++;
                index = startIndex + fileSize;
            }

            return extractedCount;
        }

        private int ExtractAampFiles(byte[] data, string baseName, string outputDir)
        {
            int index = 0;
            int fileIndex = 1;
            int extractedCount = 0;

            while (index <= data.Length - AAMP_SIGNATURE.Length)
            {
                int startIndex = IndexOf(data, AAMP_SIGNATURE, index);
                if (startIndex == -1) break;

                if (startIndex + 16 > data.Length)
                {
                    index = startIndex + 1;
                    continue;
                }

                uint fileSize = ReadLittleEndianUInt32(data, startIndex + 12);
                if (fileSize <= 0 || startIndex + fileSize > data.Length)
                {
                    index = startIndex + 1;
                    continue;
                }

                byte[] fileData = new byte[fileSize];
                Array.Copy(data, startIndex, fileData, 0, fileSize);

                string fileName = $"{baseName}_{fileIndex}.aamp";
                string outputPath = Path.Combine(outputDir, fileName);
                outputPath = GetUniqueFilePath(outputPath);

                File.WriteAllBytes(outputPath, fileData);
                extractedCount++;
                OnFileExtracted(outputPath);

                fileIndex++;
                index = startIndex + (int)fileSize;
            }

            return extractedCount;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);

            int duplicateCount = 1;
            string newFilePath;
            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }
    }
}