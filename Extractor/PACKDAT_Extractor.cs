using System.Text;

namespace super_toolbox
{    
    public class PACKDAT_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        static PACKDAT_Extractor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath} 不是有效的目录");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories).ToList();
            var archiveFiles = new List<string>();

            foreach (var file in allFiles)
            {
                if (file.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                {
                    archiveFiles.Add(file);
                }
            }

            TotalFilesToExtract = archiveFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{TotalFilesToExtract}个SYSTEM-ε PACKDAT文件");

            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var archiveFilePath in archiveFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string archiveFileName = Path.GetFileName(archiveFilePath);
                            string folderName = Path.GetFileNameWithoutExtension(archiveFileName);
                            string extractDir = Path.Combine(Path.GetDirectoryName(archiveFilePath) ?? "", folderName);

                            Directory.CreateDirectory(extractDir);

                            ExtractionProgress?.Invoke(this, $"正在处理:{archiveFileName}");

                            int extractedCount = UnpackArchive(archiveFilePath, extractDir);
                            totalExtractedFiles += extractedCount;

                            ExtractionProgress?.Invoke(this, $"完成处理:{archiveFileName}->{extractedCount}个文件");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(archiveFilePath)}时出错:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"提取完成,总共提取了{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        private int UnpackArchive(string filePath, string outputDir)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] header = new byte[8];
                fs.Read(header, 0, 8);

                uint signature = LittleEndianToUInt32(header, 0);
                if (signature != 0x4B434150)
                {
                    ExtractionError?.Invoke(this, $"不是有效的PACKDAT文件:签名错误");
                    return 0;
                }

                if (!AsciiEqual(header, 4, "DAT."))
                {
                    ExtractionError?.Invoke(this, $"不是有效的PACKDAT文件:DAT标识错误");
                    return 0;
                }

                byte[] countBuf = new byte[4];
                fs.Seek(8, SeekOrigin.Begin);
                fs.Read(countBuf, 0, 4);
                int count = LittleEndianToInt32(countBuf, 0);

                if (count <= 0 || count > 100000)
                {
                    ExtractionError?.Invoke(this, $"无效的文件数量:{count}");
                    return 0;
                }

                uint indexSize = 0x30 * (uint)count;
                if (indexSize > fs.Length - 0x10)
                {
                    ExtractionError?.Invoke(this, $"索引大小超出文件范围");
                    return 0;
                }

                var entries = new List<PackEntry>();
                long indexOffset = 0x10;

                for (int i = 0; i < count; ++i)
                {
                    fs.Seek(indexOffset, SeekOrigin.Begin);

                    byte[] nameBuf = new byte[0x20];
                    fs.Read(nameBuf, 0, 0x20);
                    string name = GetCString(nameBuf, 0, 0x20);

                    byte[] entryData = new byte[0x10];
                    fs.Read(entryData, 0, 0x10);

                    uint offset = LittleEndianToUInt32(entryData, 0);
                    uint flags = LittleEndianToUInt32(entryData, 4);
                    uint size = LittleEndianToUInt32(entryData, 8);
                    uint unpackedSize = LittleEndianToUInt32(entryData, 12);

                    if (offset + size > fs.Length)
                    {
                        ExtractionError?.Invoke(this, $"文件{name}的位置超出范围");
                        return 0;
                    }

                    entries.Add(new PackEntry
                    {
                        Name = name,
                        Offset = offset,
                        Flags = flags,
                        Size = size,
                        UnpackedSize = unpackedSize
                    });

                    indexOffset += 0x30;
                }

                return ExtractFiles(fs, entries, outputDir);
            }
        }

        private bool AsciiEqual(byte[] data, int offset, string text)
        {
            if (offset + text.Length > data.Length)
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (data[offset + i] != (byte)text[i])
                    return false;
            }
            return true;
        }

        private uint LittleEndianToUInt32(byte[] value, int index)
        {
            return (uint)(value[index] | value[index + 1] << 8 | value[index + 2] << 16 | value[index + 3] << 24);
        }

        private int LittleEndianToInt32(byte[] value, int index)
        {
            return (int)LittleEndianToUInt32(value, index);
        }

        private string GetCString(byte[] data, int index, int lengthLimit)
        {
            int nameLength = 0;
            while (nameLength < lengthLimit && index + nameLength < data.Length && data[index + nameLength] != 0)
                nameLength++;
            return Encoding.GetEncoding(932).GetString(data, index, nameLength);
        }

        private uint RotL(uint v, int count)
        {
            count &= 0x1F;
            return v << count | v >> (32 - count);
        }

        private int ExtractFiles(FileStream fs, List<PackEntry> entries, string outputDir)
        {
            int extracted = 0;

            foreach (var entry in entries)
            {
                string outputPath = Path.Combine(outputDir, entry.Name);

                byte[] data;
                if ((entry.Flags & 0x10000) != 0 || entry.Name.EndsWith(".s", StringComparison.OrdinalIgnoreCase))
                {
                    fs.Seek(entry.Offset, SeekOrigin.Begin);
                    data = new byte[entry.Size];
                    fs.Read(data, 0, (int)entry.Size);

                    if ((entry.Flags & 0x10000) != 0)
                    {
                        byte[] temp = new byte[4];
                        for (int i = 0; i < data.Length; i += 4)
                        {
                            if (i + 4 > data.Length) break;

                            uint value = LittleEndianToUInt32(data, i);
                            uint key = entry.Size >> 2;
                            key ^= key << (((int)key & 7) + 8);

                            value ^= key;

                            int cl = (int)(value % 24);

                            temp[0] = (byte)(value);
                            temp[1] = (byte)(value >> 8);
                            temp[2] = (byte)(value >> 16);
                            temp[3] = (byte)(value >> 24);

                            Array.Copy(temp, 0, data, i, 4);

                            key = RotL(key, cl);
                        }
                    }

                    if (entry.Name.EndsWith(".s", StringComparison.OrdinalIgnoreCase))
                    {
                        for (int i = 0; i < data.Length; ++i)
                            data[i] ^= 0xFF;
                    }
                }
                else
                {
                    data = new byte[entry.Size];
                    fs.Seek(entry.Offset, SeekOrigin.Begin);
                    fs.Read(data, 0, (int)entry.Size);
                }

                string dir = Path.GetDirectoryName(outputPath) ?? "";
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, data);

                extracted++;
                OnFileExtracted(entry.Name);

                if (extracted % 10 == 0 || extracted == entries.Count)
                {
                    ExtractionProgress?.Invoke(this, $"正在提取:{entry.Name} ({extracted}/{entries.Count})");
                }
            }

            return extracted;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
        internal class PackDatEntry
        {
            public string? Name { get; set; }
            public uint Offset { get; set; }
            public uint Flags { get; set; }
            public uint Size { get; set; }
            public uint UnpackedSize { get; set; }
        }
        private class PackEntry
        {
            public string Name { get; set; } = "";
            public long Offset { get; set; }
            public uint Flags { get; set; }
            public uint Size { get; set; }
            public uint UnpackedSize { get; set; }
        }
    }
}