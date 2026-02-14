using System.IO.Compression;
using System.Text;

namespace super_toolbox
{
    public class XenobladeMap_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] DAP1_HEADER = { 0x44, 0x41, 0x50, 0x31 };
        private static readonly byte[] BRRES_HEADER = { 0x62, 0x72, 0x65, 0x73 };
        private static readonly byte[] ZLIB_HEADER = { 0x78, 0x9C };

        private static readonly Dictionary<byte[], string> MAGIC_EXTENSIONS = new Dictionary<byte[], string>(new ByteArrayComparer())
        {
            { new byte[] { 0x49, 0x44, 0x44, 0x45 }, "idde" },
            { new byte[] { 0x4B, 0x59, 0x50 }, "kyp" },
            { new byte[] { 0x53, 0x54, 0x47, 0x4C }, "stgl" },
            { new byte[] { 0x4D, 0x50, 0x46, 0x46 }, "mpff" },
            { new byte[] { 0x62, 0x72, 0x65, 0x73 }, "brres" },
            { new byte[] { 0x6F, 0x63, 0x63 }, "occ" }
        };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<FileSegmentInfo> fileSegments = new List<FileSegmentInfo>();
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
                        int dapCount = FindDapSegments(content, filePath, fileSegments);
                        int brresCount = FindBrresSegments(content, filePath, fileSegments);

                        if (dapCount + brresCount > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中找到{dapCount}个DAP片段,{brresCount}个BRRES片段");
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

                if (fileSegments.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"开始处理{fileSegments.Count}个文件片段...");

                    int totalExtractedCount = 0;

                    for (int segmentIndex = 0; segmentIndex < fileSegments.Count; segmentIndex++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        var segment = fileSegments[segmentIndex];

                        try
                        {
                            int count = 0;
                            if (segment.Type == "DAP")
                            {
                                count = await ProcessDapSegmentAsync(segment, extractedDir, cancellationToken);
                            }
                            else if (segment.Type == "BRRES")
                            {
                                count = await ProcessBrresSegmentAsync(segment, extractedDir, segmentIndex + 1, cancellationToken);
                            }

                            if (count > 0)
                            {
                                totalExtractedCount += count;
                                ExtractionProgress?.Invoke(this, $"处理{segment.Type}片段提取出{count}个文件");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{segment.Type}片段时出错:{ex.Message}");
                        }
                    }

                    if (totalExtractedCount > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"处理完成,共提取出{totalExtractedCount}个文件");
                        OnExtractionCompleted();
                    }
                    else
                    {
                        ExtractionProgress?.Invoke(this, "处理完成,但未提取出任何有效文件");
                        OnExtractionFailed("未提取出任何有效文件");
                    }
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成,未找到任何文件片段");
                    OnExtractionFailed("未找到任何文件片段");
                }
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private int FindDapSegments(byte[] content, string sourceFilePath, List<FileSegmentInfo> fileSegments)
        {
            int count = 0;
            int index = 0;

            while (index < content.Length)
            {
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

                fileSegments.Add(new FileSegmentInfo
                {
                    SourceFilePath = sourceFilePath,
                    SegmentData = segmentData,
                    HeaderOffset = headerStartIndex,
                    AbsoluteOffset = headerStartIndex,
                    Type = "DAP"
                });

                count++;
                index = headerStartIndex + 1;
            }

            return count;
        }

        private int FindBrresSegments(byte[] content, string sourceFilePath, List<FileSegmentInfo> fileSegments)
        {
            int count = 0;
            int index = 0;

            while (index < content.Length)
            {
                int headerStartIndex = IndexOf(content, BRRES_HEADER, index);
                if (headerStartIndex == -1)
                {
                    break;
                }

                if (headerStartIndex + 12 > content.Length)
                {
                    index = headerStartIndex + 1;
                    continue;
                }

                int fileSize = ReadBigEndianInt32(content, headerStartIndex + 8);

                if (fileSize <= 0 || headerStartIndex + fileSize > content.Length)
                {
                    index = headerStartIndex + 1;
                    continue;
                }

                byte[] segmentData = new byte[fileSize];
                Array.Copy(content, headerStartIndex, segmentData, 0, fileSize);

                fileSegments.Add(new FileSegmentInfo
                {
                    SourceFilePath = sourceFilePath,
                    SegmentData = segmentData,
                    HeaderOffset = headerStartIndex,
                    AbsoluteOffset = headerStartIndex,
                    Type = "BRRES"
                });

                count++;
                index = headerStartIndex + fileSize;
            }

            return count;
        }

        private string DetectExtension(byte[] data)
        {
            foreach (var kv in MAGIC_EXTENSIONS)
            {
                byte[] magic = kv.Key;
                if (data.Length >= magic.Length)
                {
                    bool match = true;
                    for (int i = 0; i < magic.Length; i++)
                    {
                        if (data[i] != magic[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                        return kv.Value;
                }
            }
            return "bin";
        }

        private async Task<int> ProcessDapSegmentAsync(FileSegmentInfo dapSegment, string extractedDir, CancellationToken cancellationToken)
        {
            int count = 0;
            byte[] dapData = dapSegment.SegmentData;

            try
            {
                using (var stream = new MemoryStream(dapData))
                using (var reader = new BinaryReader(stream))
                {
                    reader.ReadBytes(4);
                    uint numFiles = ReadBigEndianUInt32(reader);

                    if (numFiles == 0 || numFiles > 100)
                        return 0;

                    List<DapFileEntry> fileEntries = new List<DapFileEntry>();

                    for (int i = 0; i < numFiles; i++)
                    {
                        if (stream.Position + 24 > stream.Length)
                            break;

                        byte[] nameBytes = reader.ReadBytes(8);
                        string fileName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                        uint offset = ReadBigEndianUInt32(reader);
                        uint compressedSize = ReadBigEndianUInt32(reader);
                        uint uncompressedSize = ReadBigEndianUInt32(reader);
                        reader.ReadBytes(4);

                        if (!string.IsNullOrEmpty(fileName))
                        {
                            fileEntries.Add(new DapFileEntry
                            {
                                Name = fileName,
                                Offset = offset,
                                CompressedSize = compressedSize,
                                UncompressedSize = uncompressedSize
                            });
                        }
                    }

                    foreach (var entry in fileEntries)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        try
                        {
                            if (entry.Offset + 8 > stream.Length)
                                continue;

                            stream.Seek(entry.Offset, SeekOrigin.Begin);
                            reader.ReadBytes(3);
                            reader.ReadByte();
                            uint sizeCheck = ReadBigEndianUInt32(reader);

                            long dataOffset = entry.Offset + 8;
                            if (dataOffset + entry.CompressedSize > stream.Length)
                                continue;

                            stream.Seek(dataOffset, SeekOrigin.Begin);
                            byte[] fileData = new byte[entry.CompressedSize];
                            await stream.ReadAsync(fileData, 0, (int)entry.CompressedSize, cancellationToken);

                            byte[] finalData;
                            if (fileData.Length >= 2 && fileData[0] == ZLIB_HEADER[0] && fileData[1] == ZLIB_HEADER[1])
                            {
                                try
                                {
                                    finalData = DecompressZlib(fileData);
                                    ExtractionProgress?.Invoke(this, $"  解压: {entry.Name} ({fileData.Length} -> {finalData.Length}字节)");
                                }
                                catch
                                {
                                    finalData = fileData;
                                }
                            }
                            else
                            {
                                finalData = fileData;
                            }

                            string extension = DetectExtension(finalData);
                            string outputFileName = $"{entry.Name}.{extension}";
                            string outputFilePath = Path.Combine(extractedDir, outputFileName);
                            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                            await File.WriteAllBytesAsync(outputFilePath, finalData, cancellationToken);
                            OnFileExtracted(outputFilePath);
                            ExtractionProgress?.Invoke(this, $"已提取:{outputFileName} ({finalData.Length}字节)");

                            count++;
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理DAP片段时出错:{ex.Message}");
            }

            return count;
        }

        private async Task<int> ProcessBrresSegmentAsync(FileSegmentInfo brresSegment, string extractedDir, int segmentIndex, CancellationToken cancellationToken)
        {
            try
            {
                byte[] brresData = brresSegment.SegmentData;
                string baseFileName = Path.GetFileNameWithoutExtension(brresSegment.SourceFilePath);
                string outputFileName = $"{baseFileName}_{segmentIndex}.brres";
                string outputFilePath = Path.Combine(extractedDir, outputFileName);

                outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);
                await File.WriteAllBytesAsync(outputFilePath, brresData, cancellationToken);

                OnFileExtracted(outputFilePath);
                ExtractionProgress?.Invoke(this, $"已提取:{outputFileName} ({brresData.Length}字节)");

                return 1;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理BRRES片段时出错:{ex.Message}");
                return 0;
            }
        }

        private byte[] DecompressZlib(byte[] compressedData)
        {
            if (compressedData.Length < 2)
                return compressedData;

            using (var compressedStream = new MemoryStream(compressedData, 2, compressedData.Length - 2))
            using (var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
            using (var resultStream = new MemoryStream())
            {
                deflateStream.CopyTo(resultStream);
                return resultStream.ToArray();
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
                    return i;
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

        private int ReadBigEndianInt32(byte[] data, int offset)
        {
            if (offset + 3 >= data.Length)
                return 0;

            return (data[offset] << 24) | (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) | data[offset + 3];
        }

        private class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[]? x, byte[]? y)
            {
                if (x == null || y == null) return false;
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (x[i] != y[i]) return false;
                return true;
            }

            public int GetHashCode(byte[] obj)
            {
                int hash = 0;
                foreach (byte b in obj)
                    hash = (hash << 8) ^ b;
                return hash;
            }
        }

        private struct FileSegmentInfo
        {
            public string SourceFilePath;
            public byte[] SegmentData;
            public int HeaderOffset;
            public int AbsoluteOffset;
            public string Type;
        }

        private struct DapFileEntry
        {
            public string Name;
            public uint Offset;
            public uint CompressedSize;
            public uint UncompressedSize;
        }
    }
}
