using System.Collections.Concurrent;
using System.Text;

namespace super_toolbox
{
    public class NintendoSound_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly AudioFormatInfo[] FormatInfos = new AudioFormatInfo[]
        {
            new AudioFormatInfo(
                name: "CWAV",
                signature: new byte[] { 0x43, 0x57, 0x41, 0x56, 0xFF, 0xFE },
                sizeOffset: 0xC,
                isBigEndian: false,
                extension: ".bcwav"
            ),
            new AudioFormatInfo(
                name: "FWAV",
                signature: new byte[] { 0x46, 0x57, 0x41, 0x56, 0xFE, 0xFF },
                sizeOffset: 0xC,
                isBigEndian: true,
                extension: ".bfwav"
            ),
            new AudioFormatInfo(
                name: "RWAR",
                signature: new byte[] { 0x52, 0x57, 0x41, 0x52 },
                sizeOffset: 0x8,
                isBigEndian: true,
                extension: ".rwar"
            ),
            new AudioFormatInfo(
                name: "RWAV",
                signature: new byte[] { 0x52, 0x57, 0x41, 0x56 },
                sizeOffset: 0x8,
                isBigEndian: true,
                extension: ".brwav"
            ),
            new AudioFormatInfo(
                name: "RWAV_REV",
                signature: new byte[] { 0x56, 0x41, 0x57, 0x52 }, // VAWR
                sizeOffset: 0x8,
                isBigEndian: false,
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
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = files.Count;

            foreach (var format in FormatInfos)
            {
                _counters[format.Name] = 0;
            }

            int processedFiles = 0;

            try
            {
                var existingFormats = new ConcurrentDictionary<string, bool>();
                ExtractionProgress?.Invoke(this, $"正在扫描文件以检测音频格式...");

                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            byte[] content = File.ReadAllBytes(filePath);

                            foreach (var format in FormatInfos)
                            {
                                if (HasSignature(content, format.Signature))
                                {
                                    existingFormats[format.Name] = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"扫描文件{Path.GetFileName(filePath)}时发生错误:{ex.Message}");
                            OnExtractionFailed($"扫描文件{Path.GetFileName(filePath)}时发生错误:{ex.Message}");
                        }
                    });
                }, cancellationToken);

                foreach (var format in FormatInfos)
                {
                    if (existingFormats.ContainsKey(format.Name))
                    {
                        string formatDir = Path.Combine(extractedDir, format.OriginalName ?? format.Name);
                        Directory.CreateDirectory(formatDir);
                        ExtractionProgress?.Invoke(this, $"创建文件夹:{format.OriginalName ?? format.Name}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"开始提取音频文件...");

                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        processedFiles++;
                        string fileName = Path.GetFileName(filePath);
                        ExtractionProgress?.Invoke(this, $"正在处理文件:{fileName} ({processedFiles}/{TotalFilesToExtract})");

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
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理文件{fileName}时发生错误:{ex.Message}");
                            OnExtractionFailed($"处理文件{fileName}时发生错误:{ex.Message}");
                        }
                    });
                }, cancellationToken);

                if (_reversibleFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"开始修复{_reversibleFiles.Count}个需要反转的RWAV文件...");
                    int fixedCount = 0;

                    foreach (var filePath in _reversibleFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            if (FixReversedBytes(filePath))
                            {
                                fixedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"修复文件{filePath}时出错:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"完成修复，共修复{fixedCount}个RWAV文件");
                }
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }

            int actualExtractedCount = 0;
            StringBuilder summary = new StringBuilder();
            summary.AppendLine("提取完成，统计信息:");

            foreach (var format in FormatInfos)
            {
                string dirName = format.OriginalName ?? format.Name;
                if (_counters[format.Name] > 0)
                {
                    string formatDir = Path.Combine(extractedDir, dirName);
                    if (Directory.Exists(formatDir))
                    {
                        int fileCount = Directory.GetFiles(formatDir, "*.*", SearchOption.TopDirectoryOnly).Length;
                        actualExtractedCount += fileCount;
                        summary.AppendLine($"  {dirName}: {_counters[format.Name]}个文件");
                    }
                }
            }

            if (actualExtractedCount > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{actualExtractedCount}个音频文件");
                ExtractionProgress?.Invoke(this, summary.ToString());
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到音频文件");
            }

            if (ExtractedFileCount != actualExtractedCount)
            {
                ExtractionError?.Invoke(this, $"警告:统计数量({ExtractedFileCount})与实际数量({actualExtractedCount})不符，可能存在文件操作异常。");
            }

            OnExtractionCompleted();
        }

        private void ExtractAudioFormat(string filePath, byte[] content, AudioFormatInfo format, string rootOutputDir, CancellationToken cancellationToken)
        {
            int index = 0;
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            int fileCountForThisFormat = 0;
            string outputDir = Path.Combine(rootOutputDir, format.OriginalName ?? format.Name);

            int localCounter = 1;

            while (index < content.Length)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int startIndex = IndexOf(content, format.Signature, index);
                if (startIndex == -1) break;

                int sizeOffset = startIndex + format.SizeOffset;
                if (sizeOffset + 4 > content.Length)
                {
                    index = startIndex + 1;
                    continue;
                }

                int fileSize = format.IsBigEndian
                    ? ReadBigEndianInt32(content, sizeOffset)
                    : ReadLittleEndianInt32(content, sizeOffset);

                if (fileSize <= 0 || startIndex + fileSize > content.Length || fileSize > 100 * 1024 * 1024)
                {
                    index = startIndex + 1;
                    continue;
                }

                byte[] audioData = new byte[fileSize];
                Array.Copy(content, startIndex, audioData, 0, fileSize);

                int counter = _counters.AddOrUpdate(format.Name, 1, (key, oldValue) => oldValue + 1);

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

                fileCountForThisFormat++;
                localCounter++; 

                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(audioFilePath)}");

                index = startIndex + fileSize;
            }

            if (fileCountForThisFormat > 0)
            {
                ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中提取出{fileCountForThisFormat}个{format.Name}文件");
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
                    { new byte[] { 0x56, 0x41, 0x57, 0x52 }, new byte[] { 0x52, 0x57, 0x41, 0x56 } }, // VAWR -> RWAV
                    { new byte[] { 0x4F, 0x46, 0x4E, 0x49 }, new byte[] { 0x49, 0x4E, 0x46, 0x4F } }, // OFNI -> INFO
                    { new byte[] { 0x41, 0x54, 0x41, 0x44 }, new byte[] { 0x44, 0x41, 0x54, 0x41 } }  // ATAD -> DATA                  
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

                    ExtractionProgress?.Invoke(this,
                        $"修复了{fixCount}处反转字节，重命名为: {Path.GetFileName(newFilePath)}");

                    return true;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"修复文件{filePath}时出错:{ex.Message}");
            }

            return false;
        }

        private bool HasSignature(byte[] content, byte[] signature)
        {
            return IndexOf(content, signature, 0) != -1;
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

        private class AudioFormatInfo
        {
            public string Name { get; }
            public byte[] Signature { get; }
            public int SizeOffset { get; }
            public bool IsBigEndian { get; }
            public string Extension { get; }
            public bool IsReversible { get; }
            public string? OriginalName { get; }

            public AudioFormatInfo(string name, byte[] signature, int sizeOffset, bool isBigEndian, string extension, bool isReversible = false, string? originalName = null)
            {
                Name = name;
                Signature = signature;
                SizeOffset = sizeOffset;
                IsBigEndian = isBigEndian;
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
