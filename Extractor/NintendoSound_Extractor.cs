using System.Collections.Concurrent;

namespace super_toolbox
{
    public class NintendoSound_Extractor : BaseExtractor
    {
        private static readonly AudioFormatInfo[] FormatInfos = new AudioFormatInfo[]
        {
            new AudioFormatInfo(
                name: "CWAV",
                signature: new byte[] { 0x43, 0x57, 0x41, 0x56 },
                byteOrderCheck: new ByteOrderCheck { Offset = 0x4, LittleEndian = new byte[] { 0xFF, 0xFE }, BigEndian = new byte[] { 0xFE, 0xFF } },
                sizeOffset: 0xC,
                infoOffset: 0x40,
                extension: ".bcwav"
            ),
            new AudioFormatInfo(
                name: "FWAV",
                signature: new byte[] { 0x46, 0x57, 0x41, 0x56 },
                byteOrderCheck: new ByteOrderCheck { Offset = 0x4, LittleEndian = new byte[] { 0xFF, 0xFE }, BigEndian = new byte[] { 0xFE, 0xFF } },
                sizeOffset: 0xC,
                infoOffset: 0x40,
                extension: ".bfwav"
            ),
            new AudioFormatInfo(
                name: "RWAV",
                signature: new byte[] { 0x52, 0x57, 0x41, 0x56 },
                byteOrderCheck: new ByteOrderCheck { Offset = 0x4, LittleEndian = new byte[] { 0xFF, 0xFE }, BigEndian = new byte[] { 0xFE, 0xFF } },
                sizeOffset: 0x8,
                infoOffset: 0x20,
                extension: ".brwav"
            ),
            new AudioFormatInfo(
                name: "RWAV_REV",
                signature: new byte[] { 0x56, 0x41, 0x57, 0x52 },
                byteOrderCheck: new ByteOrderCheck { Offset = 0x4, LittleEndian = new byte[] { 0xFF, 0xFE }, BigEndian = new byte[] { 0xFE, 0xFF } },
                sizeOffset: 0x8,
                infoOffset: 0x20,
                extension: ".brwav",
                isReversible: true,
                originalName: "RWAV"
            )
        };

        private ConcurrentDictionary<string, int> _counters = new ConcurrentDictionary<string, int>();
        private ConcurrentBag<string> _reversibleFiles = new ConcurrentBag<string>();

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = files.Count;

            foreach (var format in FormatInfos)
            {
                _counters[format.Name] = 0;
            }

            try
            {
                var existingFormats = new ConcurrentDictionary<string, bool>();

                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            byte[] content = File.ReadAllBytes(filePath);

                            foreach (var format in FormatInfos)
                            {
                                if (ContainsValidAudioFormat(content, format))
                                {
                                    existingFormats[format.Name] = true;
                                }
                            }
                        }
                        catch { }
                    });
                }, cancellationToken);

                foreach (var format in FormatInfos)
                {
                    if (existingFormats.ContainsKey(format.Name))
                    {
                        string dirName = format.OriginalName ?? format.Name;
                        string formatDir = Path.Combine(extractedDir, dirName);
                        Directory.CreateDirectory(formatDir);
                    }
                }

                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            byte[] content = File.ReadAllBytes(filePath);

                            foreach (var format in FormatInfos)
                            {
                                if (existingFormats.ContainsKey(format.Name))
                                {
                                    ExtractAudioFormat(filePath, content, format, extractedDir, cancellationToken);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch { }
                    });
                }, cancellationToken);

                if (_reversibleFiles.Count > 0)
                {
                    foreach (var filePath in _reversibleFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            FixReversedBytes(filePath);
                        }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("提取操作已取消");
                throw;
            }

            OnExtractionCompleted();
        }

        private bool ContainsValidAudioFormat(byte[] content, AudioFormatInfo format)
        {
            if (content.Length < format.Signature.Length) return false;

            int index = 0;
            while (index <= content.Length - format.Signature.Length)
            {
                int sigIndex = IndexOf(content, format.Signature, index);
                if (sigIndex == -1) break;

                if (IsValidAudioFormatAtPosition(content, sigIndex, format))
                    return true;

                index = sigIndex + 1;
            }
            return false;
        }

        private bool IsValidAudioFormatAtPosition(byte[] content, int position, AudioFormatInfo format)
        {
            if (position + format.InfoOffset + 4 > content.Length) return false;

            int byteOrderPos = position + format.ByteOrderCheck.Offset;
            if (byteOrderPos + 2 > content.Length) return false;

            byte[] byteOrder = new byte[2];
            Array.Copy(content, byteOrderPos, byteOrder, 0, 2);

            if (!byteOrder.SequenceEqual(format.ByteOrderCheck.LittleEndian) &&
                !byteOrder.SequenceEqual(format.ByteOrderCheck.BigEndian))
                return false;

            int infoPos = position + format.InfoOffset;
            if (infoPos + 4 > content.Length) return false;

            byte[] infoHeader = new byte[4];
            Array.Copy(content, infoPos, infoHeader, 0, 4);
            if (!infoHeader.SequenceEqual(new byte[] { 0x49, 0x4E, 0x46, 0x4F })) return false;

            int sizeOffset = position + format.SizeOffset;
            if (sizeOffset + 4 > content.Length) return false;

            bool isLittleEndian = byteOrder.SequenceEqual(format.ByteOrderCheck.LittleEndian);

            int fileSize = isLittleEndian
                ? ReadLittleEndianInt32(content, sizeOffset)
                : ReadBigEndianInt32(content, sizeOffset);

            if (fileSize <= 0 || position + fileSize > content.Length || fileSize > 100 * 1024 * 1024)
                return false;

            return true;
        }

        private void ExtractAudioFormat(string filePath, byte[] content, AudioFormatInfo format, string rootOutputDir, CancellationToken cancellationToken)
        {
            int index = 0;
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputDir = Path.Combine(rootOutputDir, format.OriginalName ?? format.Name);
            int localCounter = 1;

            while (index <= content.Length - format.Signature.Length)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int startIndex = IndexOf(content, format.Signature, index);
                if (startIndex == -1) break;

                if (!IsValidAudioFormatAtPosition(content, startIndex, format))
                {
                    index = startIndex + 1;
                    continue;
                }

                int byteOrderPos = startIndex + format.ByteOrderCheck.Offset;
                byte[] byteOrder = new byte[2];
                Array.Copy(content, byteOrderPos, byteOrder, 0, 2);
                bool isLittleEndian = byteOrder.SequenceEqual(format.ByteOrderCheck.LittleEndian);

                int sizeOffset = startIndex + format.SizeOffset;
                int fileSize = isLittleEndian
                    ? ReadLittleEndianInt32(content, sizeOffset)
                    : ReadBigEndianInt32(content, sizeOffset);

                if (fileSize <= 0 || startIndex + fileSize > content.Length || fileSize > 100 * 1024 * 1024)
                {
                    index = startIndex + 1;
                    continue;
                }

                byte[] audioData = new byte[fileSize];
                Array.Copy(content, startIndex, audioData, 0, fileSize);

                _counters.AddOrUpdate(format.Name, 1, (key, oldValue) => oldValue + 1);

                string audioFileName = format.IsReversible
                    ? $"{baseFileName}_{localCounter}_reversed{format.Extension}"
                    : $"{baseFileName}_{localCounter}{format.Extension}";

                string audioFilePath = Path.Combine(outputDir, audioFileName);
                audioFilePath = MakeUniqueFilename(audioFilePath);

                File.WriteAllBytes(audioFilePath, audioData);
                OnFileExtracted(audioFilePath);

                if (format.IsReversible)
                {
                    _reversibleFiles.Add(audioFilePath);
                }

                localCounter++;
                index = startIndex + fileSize;
            }
        }

        private bool FixReversedBytes(string filePath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                bool fixedSomething = false;

                Dictionary<byte[], byte[]> fixMap = new Dictionary<byte[], byte[]>(new ByteArrayComparer())
                {
                    { new byte[] { 0x56, 0x41, 0x57, 0x52 }, new byte[] { 0x52, 0x57, 0x41, 0x56 } },
                    { new byte[] { 0x4F, 0x46, 0x4E, 0x49 }, new byte[] { 0x49, 0x4E, 0x46, 0x4F } },
                    { new byte[] { 0x41, 0x54, 0x41, 0x44 }, new byte[] { 0x44, 0x41, 0x54, 0x41 } }
                };

                int fixCount = 0;

                for (int i = 0; i <= data.Length - 4; i++)
                {
                    foreach (var kvp in fixMap)
                    {
                        byte[] reversed = kvp.Key;
                        byte[] normal = kvp.Value;

                        if (data[i] == reversed[0] &&
                            data[i + 1] == reversed[1] &&
                            data[i + 2] == reversed[2] &&
                            data[i + 3] == reversed[3])
                        {
                            Array.Copy(normal, 0, data, i, 4);
                            fixCount++;
                            fixedSomething = true;
                        }
                    }
                }

                if (fixedSomething)
                {
                    string newFileName = Path.GetFileName(filePath).Replace("_reversed", "");
                    string newFilePath = Path.Combine(Path.GetDirectoryName(filePath) ?? "", newFileName);
                    newFilePath = MakeUniqueFilename(newFilePath);

                    File.WriteAllBytes(newFilePath, data);
                    File.Delete(filePath);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private int ReadBigEndianInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private int ReadLittleEndianInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
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

        private string MakeUniqueFilename(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;

            while (true)
            {
                string newPath = Path.Combine(directory, $"{fileName}_{counter}{extension}");
                if (!File.Exists(newPath))
                    return newPath;
                counter++;
            }
        }

        private class ByteOrderCheck
        {
            public int Offset { get; set; }
            public byte[] LittleEndian { get; set; } = Array.Empty<byte>();
            public byte[] BigEndian { get; set; } = Array.Empty<byte>();
        }

        private class AudioFormatInfo
        {
            public string Name { get; }
            public byte[] Signature { get; }
            public ByteOrderCheck ByteOrderCheck { get; }
            public int SizeOffset { get; }
            public int InfoOffset { get; }
            public string Extension { get; }
            public bool IsReversible { get; }
            public string? OriginalName { get; }

            public AudioFormatInfo(string name, byte[] signature, ByteOrderCheck byteOrderCheck, int sizeOffset, int infoOffset, string extension, bool isReversible = false, string? originalName = null)
            {
                Name = name;
                Signature = signature;
                ByteOrderCheck = byteOrderCheck;
                SizeOffset = sizeOffset;
                InfoOffset = infoOffset;
                Extension = extension;
                IsReversible = isReversible;
                OriginalName = originalName;
            }
        }

        private class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[]? x, byte[]? y)
            {
                if (x == null || y == null) return x == y;
                return x.SequenceEqual(y);
            }

            public int GetHashCode(byte[] obj)
            {
                int hash = 17;
                foreach (byte b in obj)
                {
                    hash = hash * 31 + b;
                }
                return hash;
            }
        }
    }
}
