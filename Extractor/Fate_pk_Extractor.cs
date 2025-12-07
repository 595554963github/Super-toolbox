using System.Text;
using System.IO.Compression;

namespace super_toolbox
{
    public class Fate_pk_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private class PkCrc32
        {
            private static readonly uint[] Table = new uint[]
            {
                0x00000000, 0x04C11DB7, 0x09823B6E, 0x0D4326D9, 0x130476DC, 0x17C56B6B, 0x1A864DB2, 0x1E475005,
                0x2608EDB8, 0x22C9F00F, 0x2F8AD6D6, 0x2B4BCB61, 0x350C9B64, 0x31CD86D3, 0x3C8EA00A, 0x384FBDBD,
                0x4C11DB70, 0x48D0C6C7, 0x4593E01E, 0x4152FDA9, 0x5F15ADAC, 0x5BD4B01B, 0x569796C2, 0x52568B75,
                0x6A1936C8, 0x6ED82B7F, 0x639B0DA6, 0x675A1011, 0x791D4014, 0x7DDC5DA3, 0x709F7B7A, 0x745E66CD,
                0x9823B6E0, 0x9CE2AB57, 0x91A18D8E, 0x95609039, 0x8B27C03C, 0x8FE6DD8B, 0x82A5FB52, 0x8664E6E5,
                0xBE2B5B58, 0xBAEA46EF, 0xB7A96036, 0xB3687D81, 0xAD2F2D84, 0xA9EE3033, 0xA4AD16EA, 0xA06C0B5D,
                0xD4326D90, 0xD0F37027, 0xDDB056FE, 0xD9714B49, 0xC7361B4C, 0xC3F706FB, 0xCEB42022, 0xCA753D95,
                0xF23A8028, 0xF6FB9D9F, 0xFBB8BB46, 0xFF79A6F1, 0xE13EF6F4, 0xE5FFEB43, 0xE8BCCD9A, 0xEC7DD02D,
                0x34867077, 0x30476DC0, 0x3D044B19, 0x39C556AE, 0x278206AB, 0x23431B1C, 0x2E003DC5, 0x2AC12072,
                0x128E9DCF, 0x164F8078, 0x1B0CA6A1, 0x1FCDBB16, 0x018AEB13, 0x054BF6A4, 0x0808D07D, 0x0CC9CDCA,
                0x7897AB07, 0x7C56B6B0, 0x71159069, 0x75D48DDE, 0x6B93DDDB, 0x6F52C06C, 0x6211E6B5, 0x66D0FB02,
                0x5E9F46BF, 0x5A5E5B08, 0x571D7DD1, 0x53DC6066, 0x4D9B3063, 0x495A2DD4, 0x44190B0D, 0x40D816BA,
                0xACA5C697, 0xA864DB20, 0xA527FDF9, 0xA1E6E04E, 0xBFA1B04B, 0xBB60ADFC, 0xB6238B25, 0xB2E29692,
                0x8AAD2B2F, 0x8E6C3698, 0x832F1041, 0x87EE0DF6, 0x99A95DF3, 0x9D684044, 0x902B669D, 0x94EA7B2A,
                0xE0B41DE7, 0xE4750050, 0xE9362689, 0xEDF73B3E, 0xF3B06B3B, 0xF771768C, 0xFA325055, 0xFEF34DE2,
                0xC6BCF05F, 0xC27DEDE8, 0xCF3ECB31, 0xCBFFD686, 0xD5B88683, 0xD1799B34, 0xDC3ABDED, 0xD8FBA05A,
                0x690CE0EE, 0x6DCDFD59, 0x608EDB80, 0x644FC637, 0x7A089632, 0x7EC98B85, 0x738AAD5C, 0x774BB0EB,
                0x4F040D56, 0x4BC510E1, 0x46863638, 0x42472B8F, 0x5C007B8A, 0x58C1663D, 0x558240E4, 0x51435D53,
                0x251D3B9E, 0x21DC2629, 0x2C9F00F0, 0x285E1D47, 0x36194D42, 0x32D850F5, 0x3F9B762C, 0x3B5A6B9B,
                0x0315D626, 0x07D4CB91, 0x0A97ED48, 0x0E56F0FF, 0x1011A0FA, 0x14D0BD4D, 0x19939B94, 0x1D528623,
                0xF12F560E, 0xF5EE4BB9, 0xF8AD6D60, 0xFC6C70D7, 0xE22B20D2, 0xE6EA3D65, 0xEBA91BBC, 0xEF68060B,
                0xD727BBB6, 0xD3E6A601, 0xDEA580D8, 0xDA649D6F, 0xC423CD6A, 0xC0E2D0DD, 0xCDA1F604, 0xC960EBB3,
                0xBD3E8D7E, 0xB9FF90C9, 0xB4BCB610, 0xB07DABA7, 0xAE3AFBA2, 0xAAFBE615, 0xA7B8C0CC, 0xA379DD7B,
                0x9B3660C6, 0x9FF77D71, 0x92B45BA8, 0x9675461F, 0x8832161A, 0x8CF30BAD, 0x81B02D74, 0x857130C3,
                0x5D8A9099, 0x594B8D2E, 0x5408ABF7, 0x50C9B640, 0x4E8EE645, 0x4A4FFBF2, 0x470CDD2B, 0x43CDC09C,
                0x7B827D21, 0x7F436096, 0x7200464F, 0x76C15BF8, 0x68860BFD, 0x6C47164A, 0x61043093, 0x65C52D24,
                0x119B4BE9, 0x155A565E, 0x18197087, 0x1CD86D30, 0x029F3D35, 0x065E2082, 0x0B1D065B, 0x0FDC1BEC,
                0x3793A651, 0x3352BBE6, 0x3E119D3F, 0x3AD08088, 0x2497D08D, 0x2056CD3A, 0x2D15EBE3, 0x29D4F654,
                0xC5A92679, 0xC1683BCE, 0xCC2B1D17, 0xC8EA00A0, 0xD6AD50A5, 0xD26C4D12, 0xDF2F6BCB, 0xDBEE767C,
                0xE3A1CBC1, 0xE760D676, 0xEA23F0AF, 0xEEE2ED18, 0xF0A5BD1D, 0xF464A0AA, 0xF9278673, 0xFDE69BC4,
                0x89B8FD09, 0x8D79E0BE, 0x803AC667, 0x84FBDBD0, 0x9ABC8BD5, 0x9E7D9662, 0x933EB0BB, 0x97FFAD0C,
                0xAFB010B1, 0xAB710D06, 0xA6322BDF, 0xA2F33668, 0xBCB4666D, 0xB8757BDA, 0xB5365D03, 0xB1F740B4
            };

            public static uint Calculate(byte[] data)
            {
                return Calculate(data, data.Length);
            }

            public static uint Calculate(byte[] data, int length)
            {
                uint crc = 0xFFFFFFFF;
                for (int i = 0; i < length; i++)
                {
                    byte index = (byte)((crc >> 24) ^ data[i]);
                    crc = (crc << 8) ^ Table[index];
                }
                return ~crc;
            }

            public static uint Calculate(string str)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(str.ToLower().Replace('\\', '/'));
                return Calculate(bytes, bytes.Length);
            }
        }

        private class PFSData
        {
            public uint DirCount { get; set; }
            public uint FileCount { get; set; }
            public List<PFSDirEntry> DirEntries { get; set; } = new List<PFSDirEntry>();
            public Dictionary<uint, string> DirNames { get; set; } = new Dictionary<uint, string>();
            public List<string> FileNames { get; set; } = new List<string>();
        }

        private class PFSDirEntry
        {
            public uint DirID { get; set; }
            public uint ParentID { get; set; }
            public uint StartChildDir { get; set; }
            public uint ChildDirCount { get; set; }
            public uint StartChildFile { get; set; }
            public uint ChildFileCount { get; set; }
        }

        private class PKHEntry
        {
            public uint Hash { get; set; }
            public ulong Offset { get; set; }
            public uint DecompressedSize { get; set; }
            public uint CompressedSize { get; set; }
        }

        private string outputDir = "Extracted";

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                var pkFiles = Directory.GetFiles(directoryPath, "*.pk");
                if (pkFiles.Length == 0)
                {
                    ExtractionError?.Invoke(this, "未找到pk文件");
                    OnExtractionFailed("未找到pk文件");
                    return;
                }

                int totalFilesFound = 0;
                int totalFilesExtracted = 0;
                int totalFilesFailed = 0;

                foreach (var pkFilePath in pkFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(pkFilePath)}");

                    try
                    {
                        var result = await ExtractPkFileAsync(pkFilePath, cancellationToken);
                        totalFilesFound += result.found;
                        totalFilesExtracted += result.extracted;
                        totalFilesFailed += result.failed;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(pkFilePath)}时出错:{ex.Message}");
                        totalFilesFailed++;
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成!总共找到{totalFilesFound}个文件,提取{totalFilesExtracted}个,失败{totalFilesFailed}个");
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
                ExtractionError?.Invoke(this, $"提取过程中出现错误:{ex.Message}");
                OnExtractionFailed($"提取过程中出现错误:{ex.Message}");
            }
        }

        private async Task<(int found, int extracted, int failed)> ExtractPkFileAsync(string pkFilePath, CancellationToken cancellationToken)
        {
            string? basePath = Path.GetDirectoryName(pkFilePath);
            if (string.IsNullOrEmpty(basePath))
            {
                throw new InvalidOperationException($"无效的文件路径:{pkFilePath}");
            }

            string baseName = Path.GetFileNameWithoutExtension(pkFilePath);
            string pfsPath = Path.Combine(basePath, baseName + ".pfs");
            string pkhPath = Path.Combine(basePath, baseName + ".pkh");

            if (!File.Exists(pfsPath))
                throw new FileNotFoundException($"pfs文件未找到:{pfsPath}");
            if (!File.Exists(pkhPath))
                throw new FileNotFoundException($"pkh文件未找到:{pkhPath}");

            string outputPath = Path.Combine(basePath, outputDir);
            Directory.CreateDirectory(outputPath);

            ExtractionProgress?.Invoke(this, $"正在读取pfs文件:{Path.GetFileName(pfsPath)}");
            PFSData pfsData = await ReadPFSFileAsync(pfsPath, cancellationToken);

            ExtractionProgress?.Invoke(this, $"正在读取pkh文件:{Path.GetFileName(pkhPath)}");
            List<PKHEntry> pkhEntries = await ReadPKHFileAsync(pkhPath, cancellationToken);

            Dictionary<uint, PKHEntry> pkhDict = pkhEntries.ToDictionary(e => e.Hash, e => e);

            int filesExtracted = 0;
            int filesFailed = 0;

            using (FileStream pkStream = File.Open(pkFilePath, FileMode.Open, FileAccess.Read))
            {
                Dictionary<uint, string> fullDirPaths = new Dictionary<uint, string>();
                BuildDirectoryPaths(pfsData, 0, "", fullDirPaths);

                for (int fileIndex = 0; fileIndex < pfsData.FileCount; fileIndex++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = pfsData.FileNames[fileIndex];
                    uint dirId = FindDirectoryForFile(pfsData, (uint)fileIndex);
                    if (dirId == uint.MaxValue)
                    {
                        ExtractionError?.Invoke(this, $"警告:无法找到文件{fileName}的目录");
                        continue;
                    }

                    string dirPath = fullDirPaths.ContainsKey(dirId) ? fullDirPaths[dirId] : "";
                    string fullPath = string.IsNullOrEmpty(dirPath) ? fileName : $"{dirPath}/{fileName}";

                    string normalizedPath = fullPath.Replace('\\', '/').ToLower();
                    uint crcHash = PkCrc32.Calculate(normalizedPath);

                    if (pkhDict.TryGetValue(crcHash, out PKHEntry? pkhEntry))
                    {
                        string outputFilePath = Path.Combine(outputPath, fullPath.Replace('/', Path.DirectorySeparatorChar));

                        string? outputDirPath = Path.GetDirectoryName(outputFilePath);
                        if (!string.IsNullOrEmpty(outputDirPath))
                            Directory.CreateDirectory(outputDirPath);

                        try
                        {
                            await ExtractFileAsync(pkStream, pkhEntry, outputFilePath, cancellationToken);
                            filesExtracted++;
                            OnFileExtracted(outputFilePath);
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"提取{fullPath}时出错:{ex.Message}");
                            filesFailed++;
                        }
                    }
                    else
                    {
                        ExtractionError?.Invoke(this, $"警告:未找到{fullPath}的哈希(CRC32:{crcHash:X8})");
                        filesFailed++;
                    }
                }
            }

            return (pfsData.FileNames.Count, filesExtracted, filesFailed);
        }

        private async Task<PFSData> ReadPFSFileAsync(string pfsPath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                using (BinaryReader reader = new BinaryReader(File.Open(pfsPath, FileMode.Open)))
                {
                    PFSData pfsData = new PFSData();

                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    pfsData.DirCount = ReadBigEndianUInt32(reader);
                    pfsData.FileCount = ReadBigEndianUInt32(reader);

                    pfsData.DirEntries = new List<PFSDirEntry>();
                    for (int i = 0; i < pfsData.DirCount; i++)
                    {
                        pfsData.DirEntries.Add(new PFSDirEntry
                        {
                            DirID = ReadBigEndianUInt32(reader),
                            ParentID = ReadBigEndianUInt32(reader),
                            StartChildDir = ReadBigEndianUInt32(reader),
                            ChildDirCount = ReadBigEndianUInt32(reader),
                            StartChildFile = ReadBigEndianUInt32(reader),
                            ChildFileCount = ReadBigEndianUInt32(reader)
                        });
                    }

                    int totalStrings = (int)(pfsData.DirCount + pfsData.FileCount);
                    List<uint> stringOffsets = new List<uint>();
                    for (int i = 0; i < totalStrings; i++)
                    {
                        stringOffsets.Add(ReadBigEndianUInt32(reader));
                    }

                    long stringTablePos = reader.BaseStream.Position;
                    byte[] stringTable = reader.ReadBytes((int)(reader.BaseStream.Length - stringTablePos));

                    pfsData.DirNames = new Dictionary<uint, string>();
                    for (int i = 0; i < pfsData.DirCount; i++)
                    {
                        uint offset = stringOffsets[i];
                        string name = ReadNullTerminatedString(stringTable, (int)offset, Encoding.ASCII);
                        pfsData.DirNames[(uint)i] = name;
                    }

                    pfsData.FileNames = new List<string>();
                    for (int i = 0; i < pfsData.FileCount; i++)
                    {
                        uint offset = stringOffsets[(int)pfsData.DirCount + i];
                        string name = ReadNullTerminatedString(stringTable, (int)offset, Encoding.ASCII);
                        pfsData.FileNames.Add(name);
                    }

                    return pfsData;
                }
            }, cancellationToken);
        }

        private async Task<List<PKHEntry>> ReadPKHFileAsync(string pkhPath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                List<PKHEntry> entries = new List<PKHEntry>();

                using (BinaryReader reader = new BinaryReader(File.Open(pkhPath, FileMode.Open)))
                {
                    uint entryCount = ReadBigEndianUInt32(reader);

                    long fileSize = reader.BaseStream.Length;
                    bool isExtellaLinkFormat = false;

                    if (fileSize == 4 + entryCount * 24)
                    {
                        isExtellaLinkFormat = true;
                    }
                    else if (fileSize != 4 + entryCount * 16)
                    {
                        isExtellaLinkFormat = (fileSize > 4 + entryCount * 20);
                    }

                    reader.BaseStream.Seek(4, SeekOrigin.Begin);

                    if (isExtellaLinkFormat)
                    {
                        for (int i = 0; i < entryCount; i++)
                        {
                            uint hash = ReadBigEndianUInt32(reader);
                            uint unknown = ReadBigEndianUInt32(reader);
                            ulong offset = ReadBigEndianUInt64(reader);
                            uint decompSize = ReadBigEndianUInt32(reader);
                            uint compSize = ReadBigEndianUInt32(reader);

                            entries.Add(new PKHEntry
                            {
                                Hash = hash,
                                Offset = offset,
                                DecompressedSize = decompSize,
                                CompressedSize = compSize
                            });
                        }
                    }
                    else
                    {
                        for (int i = 0; i < entryCount; i++)
                        {
                            uint hash = ReadBigEndianUInt32(reader);
                            uint offset = ReadBigEndianUInt32(reader);
                            uint decompSize = ReadBigEndianUInt32(reader);
                            uint compSize = ReadBigEndianUInt32(reader);

                            entries.Add(new PKHEntry
                            {
                                Hash = hash,
                                Offset = offset,
                                DecompressedSize = decompSize,
                                CompressedSize = compSize
                            });
                        }
                    }
                }

                entries.Sort((a, b) => a.Hash.CompareTo(b.Hash));

                return entries;
            }, cancellationToken);
        }

        private async Task ExtractFileAsync(FileStream pkStream, PKHEntry entry, string outputPath, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    pkStream.Seek((long)entry.Offset, SeekOrigin.Begin);

                    if (entry.CompressedSize == 0 || entry.CompressedSize == entry.DecompressedSize)
                    {
                        byte[] buffer = new byte[entry.DecompressedSize];
                        int bytesRead = pkStream.Read(buffer, 0, buffer.Length);
                        if (bytesRead != buffer.Length)
                        {
                            ExtractionError?.Invoke(this, $"警告:期望{buffer.Length}字节,实际读取{bytesRead}字节");
                        }
                        File.WriteAllBytes(outputPath, buffer);
                    }
                    else
                    {
                        byte[] compressedData = new byte[entry.CompressedSize];
                        int bytesRead = pkStream.Read(compressedData, 0, compressedData.Length);
                        if (bytesRead != compressedData.Length)
                        {
                            ExtractionError?.Invoke(this, $"警告:期望{compressedData.Length} 字节,实际读取{bytesRead}字节");
                        }

                        try
                        {
                            if (compressedData.Length >= 2 && compressedData[0] == 0x78)
                            {
                                byte[] decompressedData = DecompressZlib(compressedData, (int)entry.DecompressedSize);
                                File.WriteAllBytes(outputPath, decompressedData);
                            }
                            else if (compressedData[0] == 0x11)
                            {
                                File.WriteAllBytes(outputPath, compressedData);
                            }
                            else
                            {
                                File.WriteAllBytes(outputPath, compressedData);
                            }
                        }
                        catch
                        {
                            File.WriteAllBytes(outputPath, compressedData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"提取文件{Path.GetFileName(outputPath)}时出错:{ex.Message}", ex);
                }
            }, cancellationToken);
        }

        private byte[] DecompressZlib(byte[] compressedData, int expectedSize)
        {
            try
            {
                using (MemoryStream compressedStream = new MemoryStream(compressedData))
                using (MemoryStream decompressedStream = new MemoryStream(expectedSize))
                {
                    if (compressedData.Length >= 2 && compressedData[0] == 0x78)
                    {
                        compressedStream.Seek(2, SeekOrigin.Begin);

                        using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress, true))
                        {
                            deflateStream.CopyTo(decompressedStream);
                        }
                    }
                    else
                    {
                        using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                        {
                            deflateStream.CopyTo(decompressedStream);
                        }
                    }

                    byte[] result = decompressedStream.ToArray();
                    if (result.Length != expectedSize)
                    {
                        ExtractionError?.Invoke(this, $"警告:解压大小不匹配:期望{expectedSize},实际{result.Length}");
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Zlib解压失败:{ex.Message}", ex);
            }
        }

        private void BuildDirectoryPaths(PFSData pfsData, uint dirId, string currentPath, Dictionary<uint, string> fullDirPaths)
        {
            string dirName = pfsData.DirNames[dirId];
            string newPath = string.IsNullOrEmpty(currentPath) ? dirName : $"{currentPath}/{dirName}";
            fullDirPaths[dirId] = newPath;

            PFSDirEntry dir = pfsData.DirEntries[(int)dirId];
            for (uint i = 0; i < dir.ChildDirCount; i++)
            {
                uint childDirId = dir.StartChildDir + i;
                if (childDirId < pfsData.DirCount)
                {
                    BuildDirectoryPaths(pfsData, childDirId, newPath, fullDirPaths);
                }
            }
        }

        private uint FindDirectoryForFile(PFSData pfsData, uint fileIndex)
        {
            for (uint dirId = 0; dirId < pfsData.DirCount; dirId++)
            {
                PFSDirEntry dir = pfsData.DirEntries[(int)dirId];
                if (fileIndex >= dir.StartChildFile && fileIndex < dir.StartChildFile + dir.ChildFileCount)
                {
                    return dirId;
                }
            }
            return uint.MaxValue;
        }

        private string ReadNullTerminatedString(byte[] data, int offset, Encoding encoding)
        {
            int length = 0;
            while (offset + length < data.Length && data[offset + length] != 0)
            {
                length++;
            }
            return encoding.GetString(data, offset, length);
        }

        private uint ReadBigEndianUInt32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private ulong ReadBigEndianUInt64(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(8);
            Array.Reverse(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}