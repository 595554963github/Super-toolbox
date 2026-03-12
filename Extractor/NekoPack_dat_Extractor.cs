using System.Text;

namespace super_toolbox
{
    public class NekoPack_dat_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        static NekoPack_dat_Extractor()
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
            var datFiles = new List<string>();

            foreach (var file in allFiles)
            {
                if (file.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                {
                    datFiles.Add(file);
                }
            }

            TotalFilesToExtract = datFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{TotalFilesToExtract}个NekoPack DAT文件");

            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var datFilePath in datFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string datFileName = Path.GetFileName(datFilePath);
                            string folderName = Path.GetFileNameWithoutExtension(datFileName);
                            string datExtractDir = Path.Combine(Path.GetDirectoryName(datFilePath) ?? "", folderName);

                            Directory.CreateDirectory(datExtractDir);

                            ExtractionProgress?.Invoke(this, $"正在处理:{datFileName}");

                            int extractedCount = UnpackNekoFile(datFilePath, datExtractDir);
                            totalExtractedFiles += extractedCount;

                            ExtractionProgress?.Invoke(this, $"完成处理:{datFileName}->{extractedCount}个文件");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(datFilePath)}时出错:{ex.Message}");
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

        private int UnpackNekoFile(string filePath, string outputDir)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] header = new byte[8];
                fs.Read(header, 0, 8);

                string magic = Encoding.ASCII.GetString(header, 0, 4);
                string pack = Encoding.ASCII.GetString(header, 4, 4);

                if (magic != "NEKO" || pack != "PACK")
                {
                    ExtractionError?.Invoke(this, $"不是有效的NekoPack文件:{magic}{pack}");
                    return 0;
                }

                byte[] seedBuf = new byte[4];
                fs.Seek(0xC, SeekOrigin.Begin);
                fs.Read(seedBuf, 0, 4);
                uint seed = LittleEndianToUInt32(seedBuf, 0);
                int count = (int)(seed % 7u) + 3;

                byte[] key = new byte[0x400];
                fs.Seek(0x10, SeekOrigin.Begin);
                fs.Read(key, 0, 0x400);

                while (count-- > 0)
                {
                    Decrypt(key, 0x400, key, (ushort)seed, true);
                }

                byte[] indexSeedBuf = new byte[2];
                fs.Seek(0x410, SeekOrigin.Begin);
                fs.Read(indexSeedBuf, 0, 2);
                ushort indexSeed = LittleEndianToUInt16(indexSeedBuf, 0);

                byte[] indexInfo = new byte[8];
                fs.Seek(0x414, SeekOrigin.Begin);
                fs.Read(indexInfo, 0, 8);
                Decrypt(indexInfo, 8, key, indexSeed);

                uint indexSize = LittleEndianToUInt32(indexInfo, 0);
                long dataOffset = 0x41C + indexSize;

                byte[] indexData = new byte[indexSize];
                fs.Seek(0x41C, SeekOrigin.Begin);
                fs.Read(indexData, 0, (int)indexSize);
                Decrypt(indexData, indexData.Length, key, indexSeed);

                var entries = new List<NekoEntry>();
                int pos = 0;

                int dirCount = LittleEndianToInt32(indexData, ref pos);
                for (int d = 0; d < dirCount; ++d)
                {
                    int name_len = indexData[pos++];
                    string dir_name = GetCString(indexData, pos, name_len);
                    pos += name_len;

                    int fileCount = LittleEndianToInt32(indexData, ref pos);
                    for (int i = 0; i < fileCount; ++i)
                    {
                        pos++;
                        name_len = indexData[pos++];
                        string name = GetCString(indexData, pos, name_len);
                        pos += name_len;
                        string fullPath = name;

                        uint fileOffset = LittleEndianToUInt32(indexData, ref pos);

                        entries.Add(new NekoEntry
                        {
                            Path = fullPath,
                            Offset = dataOffset + fileOffset,
                            Key = key
                        });
                    }
                }

                byte[] buffer = new byte[12];
                foreach (var entry in entries)
                {
                    fs.Seek(entry.Offset, SeekOrigin.Begin);
                    fs.Read(buffer, 0, 2);
                    entry.FileSeed = LittleEndianToUInt16(buffer, 0);

                    fs.Seek(entry.Offset + 4, SeekOrigin.Begin);
                    fs.Read(buffer, 0, 8);
                    Decrypt(buffer, 8, key, entry.FileSeed);

                    entry.Size = LittleEndianToUInt32(buffer, 0);
                    entry.Offset += 12;
                }

                return ExtractFiles(fs, entries, outputDir);
            }
        }

        private void Decrypt(byte[] data, int length, byte[] key, ushort seed, bool init = false)
        {
            int count = length / 4;
            int pos = 0;
            while (count-- > 0)
            {
                uint s = LittleEndianToUInt32(data, pos);
                seed = (ushort)((seed + 0xC3) & 0x1FF);
                uint keyVal = LittleEndianToUInt32(key, seed);
                uint d = s ^ keyVal;
                if (init)
                    seed += (ushort)s;
                else
                    seed += (ushort)d;
                WriteUInt32(data, pos, d);
                pos += 4;
            }
        }

        private ushort LittleEndianToUInt16(byte[] value, int index)
        {
            return (ushort)(value[index] | value[index + 1] << 8);
        }

        private uint LittleEndianToUInt32(byte[] value, int index)
        {
            return (uint)(value[index] | value[index + 1] << 8 | value[index + 2] << 16 | value[index + 3] << 24);
        }

        private int LittleEndianToInt32(byte[] value, ref int index)
        {
            int val = (int)LittleEndianToUInt32(value, index);
            index += 4;
            return val;
        }

        private uint LittleEndianToUInt32(byte[] value, ref int index)
        {
            uint val = LittleEndianToUInt32(value, index);
            index += 4;
            return val;
        }

        private void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value);
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private string GetCString(byte[] data, int index, int length_limit)
        {
            int name_length = 0;
            while (name_length < length_limit && 0 != data[index + name_length])
                name_length++;
            return Encoding.GetEncoding(932).GetString(data, index, name_length);
        }

        private int ExtractFiles(FileStream fs, List<NekoEntry> entries, string outputDir)
        {
            int extracted = 0;

            foreach (var entry in entries)
            {
                string outputPath = Path.Combine(outputDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));

                byte[] data = new byte[entry.Size];
                fs.Seek(entry.Offset, SeekOrigin.Begin);
                fs.Read(data, 0, (int)entry.Size);

                Decrypt(data, data.Length, entry.Key, entry.FileSeed);

                string dir = Path.GetDirectoryName(outputPath) ?? "";
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, data);

                extracted++;
                OnFileExtracted(entry.Path);

                if (extracted % 10 == 0 || extracted == entries.Count)
                {
                    ExtractionProgress?.Invoke(this, $"正在提取:{entry.Path} ({extracted}/{entries.Count})");
                }
            }

            return extracted;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private class NekoEntry
        {
            public string Path { get; set; } = "";
            public long Offset { get; set; }
            public uint Size { get; set; }
            public byte[] Key { get; set; } = Array.Empty<byte>();
            public ushort FileSeed { get; set; }
        }
    }
}