namespace super_toolbox
{
    public class XenobladeMap_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] DAP1_HEADER = { 0x44, 0x41, 0x50, 0x31 };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<DapSegmentInfo> dapSegments = new List<DapSegmentInfo>();
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
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在扫描文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        int count = await FindDapSegmentsAsync(content, filePath, dapSegments, cancellationToken);

                        if (count > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中找到{count}个DAP片段");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        ExtractionError?.Invoke(this, $"扫描文件{filePath}时出错");
                    }
                }

                if (dapSegments.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"开始处理{dapSegments.Count}个DAP片段...");

                    int totalExtractedCount = 0;
                    int validDapSegmentCount = 0;

                    for (int segmentIndex = 0; segmentIndex < dapSegments.Count; segmentIndex++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        var dapSegment = dapSegments[segmentIndex];

                        try
                        {
                            if (!IsValidDapSegment(dapSegment.SegmentData))
                            {
                                ExtractionProgress?.Invoke(this, $"跳过无效的DAP片段 {segmentIndex + 1}");
                                continue;
                            }

                            validDapSegmentCount++;
                            int count = await ProcessDapSegmentAsync(dapSegment, extractedDir, validDapSegmentCount, cancellationToken);
                            totalExtractedCount += count;

                            ExtractionProgress?.Invoke(this, $"处理DAP片段{validDapSegmentCount}提取出{count}个文件");
                        }
                        catch (Exception)
                        {
                            ExtractionError?.Invoke(this, $"处理DAP片段时出错");
                        }
                    }

                    if (totalExtractedCount > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"处理完成，共提取出{totalExtractedCount}个文件");
                        OnExtractionCompleted();
                    }
                    else
                    {
                        ExtractionProgress?.Invoke(this, "处理完成，但未提取出任何有效文件");
                        OnExtractionFailed("未提取出任何有效文件");
                    }
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到DAP片段");
                    OnExtractionFailed("未找到DAP片段");
                }
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private bool IsValidDapSegment(byte[] segmentData)
        {
            try
            {
                if (segmentData.Length < 32)
                    return false;

                using (var stream = new MemoryStream(segmentData))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 8) return false;

                    uint fileId = ReadBigEndianUInt32(reader);
                    uint numFiles = ReadBigEndianUInt32(reader);

                    if (numFiles == 0 || numFiles > 1000)
                        return false;

                    if (stream.Length < 8 + (numFiles * 20))
                        return false;

                    for (int i = 0; i < numFiles; i++)
                    {
                        if (stream.Position + 20 > stream.Length)
                            break;

                        byte[] nameBytes = reader.ReadBytes(8);
                        string name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                        uint offset = ReadBigEndianUInt32(reader);
                        uint compressedSize = ReadBigEndianUInt32(reader);
                        uint uncompressedSize = ReadBigEndianUInt32(reader);
                        ushort unk1 = ReadBigEndianUInt16(reader);
                        ushort unk2 = ReadBigEndianUInt16(reader);

                        if (offset >= stream.Length || compressedSize == 0 || uncompressedSize == 0)
                            continue;

                        bool hasValidName = false;
                        foreach (byte b in nameBytes)
                        {
                            if (b != 0 && b >= 0x20 && b <= 0x7E)
                            {
                                hasValidName = true;
                                break;
                            }
                        }

                        if (hasValidName)
                            return true;
                    }

                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<int> FindDapSegmentsAsync(byte[] content, string sourceFilePath, List<DapSegmentInfo> dapSegments, CancellationToken cancellationToken)
        {
            int count = 0;
            int index = 0;

            while (index < content.Length)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int headerStartIndex = IndexOf(content, DAP1_HEADER, index);
                if (headerStartIndex == -1)
                {
                    break;
                }

                int nextHeaderIndex = IndexOf(content, DAP1_HEADER, headerStartIndex + 1);
                int endIndex = nextHeaderIndex == -1 ? content.Length : nextHeaderIndex;

                int segmentSize = endIndex - headerStartIndex;
                byte[] segmentData = new byte[segmentSize];
                Array.Copy(content, headerStartIndex, segmentData, 0, segmentSize);

                dapSegments.Add(new DapSegmentInfo
                {
                    SourceFilePath = sourceFilePath,
                    SegmentData = segmentData,
                    HeaderOffset = headerStartIndex
                });

                count++;
                index = headerStartIndex + 1;
            }

            return count;
        }

        private async Task<int> ProcessDapSegmentAsync(DapSegmentInfo dapSegment, string extractedDir, int segmentIndex, CancellationToken cancellationToken)
        {
            int count = 0;
            byte[] dapData = dapSegment.SegmentData;

            try
            {
                using (var stream = new MemoryStream(dapData))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 8) return 0;

                    uint fileId = ReadBigEndianUInt32(reader);
                    uint numFiles = ReadBigEndianUInt32(reader);

                    var fileEntries = new List<Dap1FileEntry>();
                    for (int i = 0; i < numFiles; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        if (stream.Position + 20 > stream.Length)
                            break;

                        byte[] nameBytes = reader.ReadBytes(8);
                        string rawName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                        if (!IsValidFileName(rawName, rawName))
                        {
                            reader.ReadBytes(12);
                            reader.ReadBytes(4);
                            continue;
                        }

                        uint offset = ReadBigEndianUInt32(reader);
                        uint compressedSize = ReadBigEndianUInt32(reader);
                        uint uncompressedSize = ReadBigEndianUInt32(reader);
                        ushort unk1 = ReadBigEndianUInt16(reader);
                        ushort unk2 = ReadBigEndianUInt16(reader);

                        fileEntries.Add(new Dap1FileEntry
                        {
                            Name = rawName,
                            RawName = rawName,
                            NameBytes = nameBytes,
                            Offset = offset,
                            CompressedSize = compressedSize,
                            UncompressedSize = uncompressedSize,
                            Unknown1 = unk1,
                            Unknown2 = unk2
                        });
                    }

                    string baseFileName = Path.GetFileNameWithoutExtension(dapSegment.SourceFilePath);
                    int fileIndex = 1;

                    foreach (var entry in fileEntries)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        try
                        {
                            if (!IsValidFileName(entry.Name, entry.RawName))
                            {
                                continue;
                            }

                            if (entry.Offset >= stream.Length)
                            {
                                continue;
                            }

                            stream.Seek(entry.Offset, SeekOrigin.Begin);

                            byte[] extBytes = reader.ReadBytes(3);
                            string rawExt = System.Text.Encoding.ASCII.GetString(extBytes).TrimEnd('\0');

                            if (!IsValidExtension(rawExt, rawExt))
                            {
                                continue;
                            }

                            byte unk3 = reader.ReadByte();
                            uint uncompressedSize2 = ReadBigEndianUInt32(reader);

                            uint dataOffset = entry.Offset + 8;

                            if (dataOffset + entry.CompressedSize > stream.Length)
                            {
                                continue;
                            }

                            byte[] compressedData = new byte[entry.CompressedSize];
                            stream.Seek(dataOffset, SeekOrigin.Begin);
                            stream.Read(compressedData, 0, (int)entry.CompressedSize);

                            byte[] decompressedData;
                            if (entry.CompressedSize == entry.UncompressedSize || entry.CompressedSize == uncompressedSize2)
                            {
                                decompressedData = compressedData;
                            }
                            else
                            {
                                decompressedData = await DecompressZlibAsync(compressedData, (int)entry.UncompressedSize, cancellationToken);
                            }

                            string outputFileName = $"{baseFileName}_{segmentIndex}_{fileIndex}_{entry.Name}.{rawExt}";
                            string outputFilePath = Path.Combine(extractedDir, outputFileName);
                            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                            await File.WriteAllBytesAsync(outputFilePath, decompressedData, cancellationToken);
                            OnFileExtracted(outputFilePath);
                            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)}");

                            count++;
                            fileIndex++;
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            catch
            {
                ExtractionError?.Invoke(this, $"处理DAP片段{segmentIndex}时出错");
            }

            return count;
        }

        private bool IsValidFileName(string cleanedName, string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return false;

            bool hasPrintableChars = false;
            foreach (char c in rawName)
            {
                if (c != 0 && c != '\0' && c >= 0x20 && c <= 0x7E)
                {
                    hasPrintableChars = true;
                    break;
                }
            }

            if (!hasPrintableChars)
                return false;

            if (string.IsNullOrWhiteSpace(cleanedName) || cleanedName == "unknown")
                return false;

            if (cleanedName.Contains("??") || cleanedName.Contains("**") || cleanedName.All(c => c == '_'))
                return false;

            return true;
        }

        private bool IsValidExtension(string cleanedExt, string rawExt)
        {
            if (string.IsNullOrWhiteSpace(rawExt))
                return false;

            bool hasPrintableChars = false;
            foreach (char c in rawExt)
            {
                if (c != 0 && c != '\0' && c >= 0x20 && c <= 0x7E && char.IsLetterOrDigit(c))
                {
                    hasPrintableChars = true;
                    break;
                }
            }

            if (!hasPrintableChars)
                return false;

            if (string.IsNullOrWhiteSpace(cleanedExt) || cleanedExt == "unknown" || cleanedExt.Length > 4 || cleanedExt.Length < 1)
                return false;

            foreach (char c in cleanedExt)
            {
                if (!char.IsLetterOrDigit(c))
                    return false;
            }

            return true;
        }

        private async Task<byte[]> DecompressZlibAsync(byte[] compressedData, int expectedSize, CancellationToken cancellationToken)
        {
            try
            {
                if (compressedData.Length >= 2 && compressedData[0] == 0x78)
                {
                    using (var compressedStream = new MemoryStream(compressedData, 2, compressedData.Length - 2))
                    using (var decompressionStream = new System.IO.Compression.DeflateStream(compressedStream, System.IO.Compression.CompressionMode.Decompress))
                    using (var resultStream = new MemoryStream())
                    {
                        byte[] buffer = new byte[4096];
                        int bytesRead;

                        while ((bytesRead = await decompressionStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            ThrowIfCancellationRequested(cancellationToken);
                            resultStream.Write(buffer, 0, bytesRead);
                        }

                        return resultStream.ToArray();
                    }
                }
                else
                {
                    return compressedData;
                }
            }
            catch
            {
                return compressedData;
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
            }
            while (File.Exists(newPath));

            return newPath;
        }

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

        private uint ReadBigEndianUInt32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private ushort ReadBigEndianUInt16(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt16(bytes, 0);
        }

        private struct DapSegmentInfo
        {
            public string SourceFilePath;
            public byte[] SegmentData;
            public int HeaderOffset;
        }

        private struct Dap1FileEntry
        {
            public string Name;
            public string RawName;
            public byte[] NameBytes;
            public uint Offset;
            public uint CompressedSize;
            public uint UncompressedSize;
            public ushort Unknown1;
            public ushort Unknown2;
        }
    }
}