using System.Text;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;

namespace super_toolbox
{
    public class Vpp_pc_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const uint VPP_MAGIC = 0x51890ACE;
        private const uint VPP_VERSION = 0x11;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var vppFiles = Directory.EnumerateFiles(directoryPath, "*.vpp_pc", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(directoryPath, "*.vpp_ps4", SearchOption.TopDirectoryOnly))
                .ToList();

            TotalFilesToExtract = vppFiles.Count;

            int processedFiles = 0;
            int totalExtractedCount = 0;

            foreach (var vppFilePath in vppFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                processedFiles++;

                try
                {
                    ExtractionProgress?.Invoke(this, $"正在处理({processedFiles}/{vppFiles.Count}): {Path.GetFileName(vppFilePath)}");

                    int extractedCount = await ProcessVppFileAsync(vppFilePath, cancellationToken, extractedFiles);
                    totalExtractedCount += extractedCount;

                    ExtractionProgress?.Invoke(this, $"{Path.GetFileName(vppFilePath)} 提取完成:{extractedCount}个文件");
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理{Path.GetFileName(vppFilePath)}失败:{ex.Message}");
                    OnExtractionFailed($"处理{Path.GetFileName(vppFilePath)}失败:{ex.Message}");
                }
            }

            if (totalExtractedCount > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{totalExtractedCount}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到有效VPP文件");
            }

            OnExtractionCompleted();
        }

        private async Task<int> ProcessVppFileAsync(string vppFilePath, CancellationToken cancellationToken, List<string> extractedFiles)
        {
            int extractedCount = 0;

            try
            {
                byte[] content = await File.ReadAllBytesAsync(vppFilePath, cancellationToken);

                using (var ms = new MemoryStream(content))
                using (var reader = new BinaryReader(ms))
                {
                    uint magic = reader.ReadUInt32();
                    if (magic != VPP_MAGIC)
                    {
                        throw new InvalidDataException($"无效的VPP文件:魔术字不匹配(期望0x{VPP_MAGIC:X8},实际0x{magic:X8})");
                    }

                    uint version = reader.ReadUInt32();
                    if (version != VPP_VERSION)
                    {
                        ExtractionProgress?.Invoke(this, $"警告:不支持的VPP版本(0x{version:X8}),尝试继续...");
                    }

                    reader.ReadInt64();

                    uint fileCount = reader.ReadUInt32();

                    reader.ReadUInt32();

                    uint namesOffset = reader.ReadUInt32();
                    uint namesSize = reader.ReadUInt32();

                    reader.ReadInt64();
                    reader.ReadInt64();
                    reader.ReadInt64();
                    reader.ReadInt64();

                    long baseOffset = reader.ReadInt64();

                    reader.ReadBytes(0x30);

                    long currentPosition = ms.Position;

                    long namesAbsoluteOffset = currentPosition + namesOffset;
                    ms.Seek(namesAbsoluteOffset, SeekOrigin.Begin);
                    byte[] namesData = reader.ReadBytes((int)namesSize);

                    string vppDir = Path.GetDirectoryName(vppFilePath) ?? string.Empty;
                    string vppName = Path.GetFileNameWithoutExtension(vppFilePath);
                    string outputDir = Path.Combine(vppDir, vppName);
                    Directory.CreateDirectory(outputDir);

                    ExtractionProgress?.Invoke(this, $"VPP信息:{fileCount}个文件,名称表:{namesSize}字节");

                    ms.Seek(currentPosition, SeekOrigin.Begin);

                    var fileEntries = new List<VppFileEntry>();
                    for (int i = 0; i < fileCount; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        var entry = new VppFileEntry
                        {
                            NameOffset = reader.ReadInt64(),
                            PathOffset = reader.ReadInt64(),
                            DataOffset = reader.ReadInt64(),
                            UncompressedSize = reader.ReadInt64(),
                            CompressedSize = reader.ReadInt64(),
                            Flags = reader.ReadInt64()
                        };

                        fileEntries.Add(entry);

                        if ((i + 1) % 100 == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"已加载条目:{i + 1}/{fileCount}");
                        }
                    }

                    for (int i = 0; i < fileEntries.Count; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        var entry = fileEntries[i];

                        try
                        {
                            string pathPart = ReadNullTerminatedString(namesData, (int)entry.PathOffset);
                            string namePart = ReadNullTerminatedString(namesData, (int)entry.NameOffset);

                            string fullPath;
                            if (!string.IsNullOrEmpty(pathPart) && !string.IsNullOrEmpty(namePart))
                            {
                                fullPath = $"{pathPart}/{namePart}";
                            }
                            else if (!string.IsNullOrEmpty(namePart))
                            {
                                fullPath = namePart;
                            }
                            else
                            {
                                fullPath = $"file_{i}";
                            }

                            fullPath = SanitizeFilePath(fullPath);

                            string outputFilePath = Path.Combine(outputDir, fullPath);
                            string outputDirPath = Path.GetDirectoryName(outputFilePath) ?? string.Empty;

                            if (!string.IsNullOrEmpty(outputDirPath))
                            {
                                Directory.CreateDirectory(outputDirPath);
                            }

                            long absoluteDataOffset = entry.DataOffset + baseOffset;

                            bool isCompressed = (entry.Flags & 0x1) != 0;

                            ExtractionProgress?.Invoke(this, $"提取:{fullPath}({entry.UncompressedSize}字节,压缩:{isCompressed})");

                            ms.Seek(absoluteDataOffset, SeekOrigin.Begin);

                            if (isCompressed)
                            {
                                byte[] compressedData = reader.ReadBytes((int)entry.CompressedSize);
                                byte[] decompressedData = DecompressLZ4F(compressedData, (int)entry.UncompressedSize);
                                File.WriteAllBytes(outputFilePath, decompressedData);
                            }
                            else
                            {
                                using (var output = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                                {
                                    byte[] buffer = new byte[81920];
                                    long remaining = entry.UncompressedSize;
                                    while (remaining > 0)
                                    {
                                        int read = reader.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                                        if (read <= 0) break;
                                        output.Write(buffer, 0, read);
                                        remaining -= read;
                                    }
                                }
                            }

                            extractedCount++;
                            if (!extractedFiles.Contains(outputFilePath))
                            {
                                extractedFiles.Add(outputFilePath);
                                OnFileExtracted(outputFilePath);
                                ExtractionProgress?.Invoke(this, $"已提取:{fullPath}");
                            }

                            if ((i + 1) % 50 == 0)
                            {
                                ExtractionProgress?.Invoke(this, $"已提取:{i + 1}/{fileEntries.Count}个文件");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"提取条目{i}失败:{ex.Message}");
                            OnExtractionFailed($"提取条目{i}失败:{ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理VPP文件失败:{ex.Message}");
                OnExtractionFailed($"处理VPP文件失败:{ex.Message}");
                throw;
            }

            return extractedCount;
        }

        private string ReadNullTerminatedString(byte[] data, int offset)
        {
            if (offset < 0 || offset >= data.Length)
                return string.Empty;

            int end = offset;
            while (end < data.Length && data[end] != 0)
                end++;

            return Encoding.UTF8.GetString(data, offset, end - offset);
        }

        private string SanitizeFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            path = path.Replace('\\', '/');

            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                char[] invalidChars = Path.GetInvalidFileNameChars();
                foreach (char invalidChar in invalidChars)
                {
                    parts[i] = parts[i].Replace(invalidChar, '_');
                }

                while (parts[i].StartsWith("."))
                {
                    parts[i] = parts[i].Substring(1);
                }

                parts[i] = parts[i].Trim();
            }

            path = string.Join(Path.DirectorySeparatorChar.ToString(), parts);

            return path;
        }

        private byte[] DecompressLZ4F(byte[] compressedData, int expectedSize)
        {
            try
            {
                byte[] decompressedData = new byte[expectedSize];
                int decompressedSize = LZ4Codec.Decode(
                    compressedData, 0, compressedData.Length,
                    decompressedData, 0, expectedSize);

                if (decompressedSize == expectedSize)
                {
                    return decompressedData;
                }
                else if (decompressedSize > 0)
                {
                    Array.Resize(ref decompressedData, decompressedSize);
                    return decompressedData;
                }
            }
            catch (Exception ex)
            {
                ExtractionProgress?.Invoke(this, $"LZ4Codec解压失败:{ex.Message},尝试使用LZ4Stream");
            }

            try
            {
                using (var input = new MemoryStream(compressedData))
                using (var decoder = LZ4Stream.Decode(input))
                using (var output = new MemoryStream())
                {
                    decoder.CopyTo(output);
                    byte[] decompressed = output.ToArray();

                    if (decompressed.Length == expectedSize)
                        return decompressed;

                    if (decompressed.Length < expectedSize)
                        throw new InvalidDataException($"解压不完整:期望{expectedSize}, 实际{decompressed.Length}");

                    Array.Resize(ref decompressed, expectedSize);
                    return decompressed;
                }
            }
            catch (Exception ex)
            {
                ExtractionProgress?.Invoke(this, $"LZ4Stream解压失败:{ex.Message}");
            }

            if (compressedData.Length == expectedSize)
            {
                ExtractionProgress?.Invoke(this, $"使用原始数据替代解压失败文件");
                return compressedData;
            }

            throw new InvalidOperationException($"无法解压LZ4数据:期望大小{expectedSize},压缩数据大小{compressedData.Length}");
        }

        private class VppFileEntry
        {
            public long NameOffset { get; set; }
            public long PathOffset { get; set; }
            public long DataOffset { get; set; }
            public long UncompressedSize { get; set; }
            public long CompressedSize { get; set; }
            public long Flags { get; set; }
        }
    }
}