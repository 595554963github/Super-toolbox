using System.Text;

namespace super_toolbox
{
    public class PBG_Extractor : BaseExtractor
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
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var datFiles = Directory.EnumerateFiles(directoryPath, "*.dat", SearchOption.AllDirectories);

            TotalFilesToExtract = datFiles.Count();
            int processedFiles = 0;

            foreach (var datFile in datFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(datFile)}");

                try
                {
                    string outputDir = Path.Combine(Path.GetDirectoryName(datFile) ?? directoryPath,
                                                   $"{Path.GetFileNameWithoutExtension(datFile)}");
                    Directory.CreateDirectory(outputDir);

                    using (FileStream fs = new FileStream(datFile, FileMode.Open, FileAccess.Read))
                    {
                        await ProcessDatFile(fs, datFile, outputDir, extractedFiles, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{datFile}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{datFile}时出错:{e.Message}");
                }

                processedFiles++;
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到有效文件");
            }
            OnExtractionCompleted();
        }

        private async Task ProcessDatFile(Stream datStream, string datFilePath, string outputDir,
                                         List<string> extractedFiles, CancellationToken cancellationToken)
        {
            byte[] magic = new byte[4];
            if (datStream.Read(magic, 0, 4) != 4)
                throw new InvalidDataException("文件太小");

            datStream.Seek(0, SeekOrigin.Begin);
            string magicStr = Encoding.ASCII.GetString(magic);
            int version = magicStr == "PBG3" ? 6 : magicStr == "PBG4" ? 7 : throw new InvalidDataException("未知的PBG格式");

            ThDat dat = new ThDat(datStream, version);
            if (!dat.Open())
                throw new InvalidDataException("无法打开DAT文件");

            ExtractionProgress?.Invoke(this, $"发现{dat.EntryCount}个条目");

            for (int i = 0; i < dat.EntryCount; i++)
            {
                var entry = dat[i];
                string outputPath = Path.Combine(outputDir, entry.Name);
                string outputDirPath = Path.GetDirectoryName(outputPath) ?? outputDir;
                Directory.CreateDirectory(outputDirPath);

                using (FileStream outFile = new FileStream(outputPath, FileMode.Create))
                {
                    long bytesWritten = dat.ReadEntry(i, outFile);
                    if (bytesWritten > 0)
                    {
                        extractedFiles.Add(outputPath);
                        OnFileExtracted(outputPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{entry.Name}");
                    }
                }
            }
        }
    }

    public class BitStream
    {
        private Stream stream;
        private uint currentByte;
        private int bitsAvailable;
        private long byteCount;

        public BitStream(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            currentByte = 0;
            bitsAvailable = 0;
            byteCount = 0;
        }

        public uint ReadBits(int bits)
        {
            if (bits > 25)
            {
                uint r = ReadBits(24);
                bits -= 24;
                return (r << bits) | ReadBits(bits);
            }

            while (bits > bitsAvailable)
            {
                int b = stream.ReadByte();
                if (b == -1) throw new EndOfStreamException();
                currentByte = (currentByte << 8) | (byte)b;
                bitsAvailable += 8;
                byteCount++;
            }

            bitsAvailable -= bits;
            return (currentByte >> bitsAvailable) & ((1u << bits) - 1);
        }

        public void WriteBit(uint bit)
        {
            currentByte = (currentByte << 1) | (bit & 1);
            bitsAvailable++;

            if (bitsAvailable == 8)
            {
                stream.WriteByte((byte)currentByte);
                bitsAvailable = 0;
                currentByte = 0;
                byteCount++;
            }
        }

        public void WriteBits(int bits, uint data)
        {
            if (bits > 32) bits = 32;
            for (int i = bits - 1; i >= 0; i--)
            {
                uint bit = (data >> i) & 1;
                WriteBit(bit);
            }
        }

        public void Finish()
        {
            while (bitsAvailable > 0)
                WriteBit(0);
        }

        public long ByteCount => byteCount;
    }

    public class ThLZSS
    {
        private const int DICT_SIZE = 0x2000;
        private const int DICT_MASK = 0x1FFF;
        private const int MIN_MATCH = 3;
        private const int MAX_MATCH = 18;
        private const int HASH_SIZE = 0x10000;
        private const uint HASH_NULL = 0;

        private class HashTable
        {
            public uint[] hash = new uint[HASH_SIZE];
            public uint[] prev = new uint[DICT_SIZE];
            public uint[] next = new uint[DICT_SIZE];

            public HashTable()
            {
                Array.Fill(hash, HASH_NULL);
                Array.Fill(prev, HASH_NULL);
                Array.Fill(next, HASH_NULL);
            }
        }

        private static uint GenerateKey(byte[] dict, uint baseIndex)
        {
            return ((uint)(dict[(baseIndex + 1) & DICT_MASK] << 8) |
                     dict[(baseIndex + 2) & DICT_MASK]) ^ (uint)(dict[baseIndex] << 4);
        }

        private static void ListRemove(HashTable hash, uint key, uint offset)
        {
            hash.next[hash.prev[offset]] = HASH_NULL;
            if (hash.prev[offset] == HASH_NULL)
                if (hash.hash[key] == offset)
                    hash.hash[key] = HASH_NULL;
        }

        private static void ListAdd(HashTable hash, uint key, uint offset)
        {
            hash.next[offset] = hash.hash[key];
            hash.prev[offset] = HASH_NULL;
            hash.prev[hash.hash[key]] = offset;
            hash.hash[key] = offset;
        }

        public static long Compress(Stream input, long inputSize, Stream output)
        {
            BitStream bs = new BitStream(output);
            HashTable hash = new HashTable();
            byte[] dict = new byte[DICT_SIZE];
            uint dictHead = 1;
            uint dictHeadKey;
            uint waitingBytes = 0;
            long bytesRead = 0;

            for (uint i = 0; i < MAX_MATCH && i < inputSize; i++)
            {
                int b = input.ReadByte();
                if (b == -1) break;
                dict[dictHead + i] = (byte)b;
                waitingBytes++;
                bytesRead++;
            }

            dictHeadKey = GenerateKey(dict, dictHead);

            while (waitingBytes > 0)
            {
                uint matchLen = MIN_MATCH - 1;
                uint matchOffset = 0;

                for (uint offset = hash.hash[dictHeadKey];
                     offset != HASH_NULL && waitingBytes > matchLen;
                     offset = hash.next[offset])
                {
                    if (dict[(dictHead + matchLen) & DICT_MASK] == dict[(offset + matchLen) & DICT_MASK])
                    {
                        uint i = 0;
                        for (; i < matchLen && dict[(dictHead + i) & DICT_MASK] == dict[(offset + i) & DICT_MASK]; i++) ;
                        if (i < matchLen) continue;

                        for (matchLen++; matchLen < waitingBytes && dict[(dictHead + matchLen) & DICT_MASK] == dict[(offset + matchLen) & DICT_MASK]; matchLen++) ;
                        matchOffset = offset;
                    }
                }

                if (matchLen < MIN_MATCH)
                {
                    matchLen = 1;
                    bs.WriteBit(1);
                    bs.WriteBits(8, dict[dictHead]);
                }
                else
                {
                    bs.WriteBit(0);
                    bs.WriteBits(13, matchOffset);
                    bs.WriteBits(4, matchLen - MIN_MATCH);
                }

                for (uint i = 0; i < matchLen; i++)
                {
                    uint offset = (dictHead + MAX_MATCH) & DICT_MASK;
                    if (offset != HASH_NULL)
                        ListRemove(hash, GenerateKey(dict, offset), offset);
                    if (dictHead != HASH_NULL)
                        ListAdd(hash, dictHeadKey, dictHead);

                    if (bytesRead < inputSize)
                    {
                        int b = input.ReadByte();
                        if (b != -1)
                        {
                            dict[offset] = (byte)b;
                            bytesRead++;
                        }
                        else
                        {
                            waitingBytes--;
                        }
                    }
                    else
                    {
                        waitingBytes--;
                    }

                    dictHead = (dictHead + 1) & DICT_MASK;
                    dictHeadKey = GenerateKey(dict, dictHead);
                }
            }

            bs.WriteBit(0);
            bs.WriteBits(13, HASH_NULL);
            bs.WriteBits(4, 0);
            bs.Finish();

            return bs.ByteCount;
        }

        public static long Decompress(Stream input, Stream output, long outputSize)
        {
            byte[] dict = new byte[DICT_SIZE];
            uint dictHead = 1;
            long bytesWritten = 0;
            BitStream bs = new BitStream(input);

            while (bytesWritten < outputSize)
            {
                if (bs.ReadBits(1) == 1)
                {
                    byte b = (byte)bs.ReadBits(8);
                    output.WriteByte(b);
                    bytesWritten++;
                    dict[dictHead] = b;
                    dictHead = (dictHead + 1) & DICT_MASK;
                }
                else
                {
                    uint matchOffset = bs.ReadBits(13);
                    if (matchOffset == 0) return bytesWritten;

                    uint matchLen = bs.ReadBits(4) + MIN_MATCH;

                    for (uint i = 0; i < matchLen; i++)
                    {
                        byte b = dict[(matchOffset + i) & DICT_MASK];
                        output.WriteByte(b);
                        bytesWritten++;
                        dict[dictHead] = b;
                        dictHead = (dictHead + 1) & DICT_MASK;
                    }
                }
            }

            return bytesWritten;
        }
    }

    public class ThDatEntry
    {
        public string Name;
        public long Offset;
        public long Size;
        public long ZSize;
        public long Extra;

        public ThDatEntry()
        {
            Name = "";
            Offset = 0;
            Size = 0;
            ZSize = 0;
            Extra = 0;
        }
    }

    public class ThDat
    {
        private Stream stream;
        private List<ThDatEntry> entries = new List<ThDatEntry>();
        private long offset;
        private int version;

        public int EntryCount => entries.Count;

        public ThDatEntry this[int index] => entries[index];

        public ThDat(Stream stream, int version)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.version = version;
        }

        private uint ReadUInt32(BitStream b)
        {
            uint size = b.ReadBits(2);
            return b.ReadBits((int)((size + 1) * 8));
        }

        private void WriteUInt32(BitStream b, uint value)
        {
            if (b == null) return;

            int size = 1;
            if ((value & 0xFFFFFF00) != 0)
            {
                size = 2;
                if ((value & 0xFFFF0000) != 0)
                {
                    size = 3;
                    if ((value & 0xFF000000) != 0)
                        size = 4;
                }
            }

            b.WriteBits(2, (uint)(size - 1));
            b.WriteBits(size * 8, value);
        }

        private string ReadString(BitStream b, int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                byte c = (byte)b.ReadBits(8);
                if (c == 0) break;
                data[i] = c;
            }
            return Encoding.ASCII.GetString(data).TrimEnd('\0');
        }

        private void WriteString(BitStream b, string str)
        {
            byte[] data = Encoding.ASCII.GetBytes(str + '\0');
            foreach (byte c in data)
                b.WriteBits(8, c);
        }

        public bool Open()
        {
            byte[] magic = new byte[4];
            if (stream.Read(magic, 0, 4) != 4)
                return false;

            string magicStr = Encoding.ASCII.GetString(magic);

            if (magicStr == "PBG3")
            {
                BitStream b = new BitStream(stream);
                uint entryCount = ReadUInt32(b);
                offset = ReadUInt32(b);

                stream.Seek(offset, SeekOrigin.Begin);
                b = new BitStream(stream);

                for (uint i = 0; i < entryCount; i++)
                {
                    ThDatEntry entry = new ThDatEntry();
                    ReadUInt32(b);
                    ReadUInt32(b);
                    entry.Extra = ReadUInt32(b);
                    entry.Offset = ReadUInt32(b);
                    entry.Size = ReadUInt32(b);
                    entry.Name = ReadString(b, 255);
                    entries.Add(entry);
                }
            }
            else if (magicStr == "PBG4")
            {
                byte[] headerBuf = new byte[12];
                if (stream.Read(headerBuf, 0, 12) != 12)
                    return false;

                uint count = BitConverter.ToUInt32(headerBuf, 0);
                uint off = BitConverter.ToUInt32(headerBuf, 4);
                uint size = BitConverter.ToUInt32(headerBuf, 8);

                stream.Seek(off, SeekOrigin.Begin);

                using (MemoryStream ms = new MemoryStream())
                {
                    ThLZSS.Decompress(stream, ms, size);
                    byte[] decompressed = ms.ToArray();

                    int pos = 0;
                    for (uint i = 0; i < count; i++)
                    {
                        ThDatEntry entry = new ThDatEntry();
                        int nameLen = 0;
                        while (decompressed[pos + nameLen] != 0) nameLen++;
                        entry.Name = Encoding.ASCII.GetString(decompressed, pos, nameLen);
                        pos += nameLen + 1;

                        entry.Offset = BitConverter.ToUInt32(decompressed, pos); pos += 4;
                        entry.Size = BitConverter.ToUInt32(decompressed, pos); pos += 4;
                        entry.Extra = BitConverter.ToUInt32(decompressed, pos); pos += 4;
                        entries.Add(entry);
                    }
                }
            }
            else
            {
                throw new InvalidDataException("未知的PBG格式");
            }

            long endOffset = stream.Seek(0, SeekOrigin.End);
            if (entries.Count > 0)
            {
                for (int i = 0; i < entries.Count - 1; i++)
                {
                    entries[i].ZSize = entries[i + 1].Offset - entries[i].Offset;
                }
                entries[^1].ZSize = endOffset - entries[^1].Offset;
            }

            return true;
        }

        public long ReadEntry(int index, Stream output)
        {
            ThDatEntry entry = entries[index];
            byte[] zdata = new byte[entry.ZSize];
            lock (stream)
            {
                stream.Seek(entry.Offset, SeekOrigin.Begin);
                if (stream.Read(zdata, 0, (int)entry.ZSize) != entry.ZSize)
                    return -1;
            }

            using (MemoryStream zstream = new MemoryStream(zdata))
            {
                return ThLZSS.Decompress(zstream, output, entry.Size);
            }
        }

        public bool Create()
        {
            offset = version == 6 ? 13 : 16;
            stream.Seek(offset, SeekOrigin.Begin);
            return true;
        }

        public long WriteEntry(int index, Stream input, long inputLength)
        {
            ThDatEntry entry = entries[index];
            entry.Size = inputLength;

            using (MemoryStream zstream = new MemoryStream())
            {
                long zsize = ThLZSS.Compress(input, entry.Size, zstream);
                if (zsize == -1) return -1;
                entry.ZSize = zsize;

                byte[] zdata = zstream.ToArray();
                if (version == 6)
                {
                    entry.Extra = 0;
                    foreach (byte b in zdata)
                        entry.Extra += b;
                }

                lock (stream)
                {
                    stream.Write(zdata, 0, (int)entry.ZSize);
                    entry.Offset = offset;
                    offset += entry.ZSize;
                }

                return entry.ZSize;
            }
        }

        public bool Close()
        {
            string magic = version == 6 ? "PBG3" : "PBG4";
            MemoryStream? buffer = null;
            BitStream? b = null;

            if (version == 6)
            {
                b = new BitStream(stream);
            }
            else
            {
                buffer = new MemoryStream();
            }

            foreach (ThDatEntry entry in entries)
            {
                if (version == 6)
                {
                    WriteUInt32(b!, 0);
                    WriteUInt32(b!, 0);
                    WriteUInt32(b!, (uint)entry.Extra);
                    WriteUInt32(b!, (uint)entry.Offset);
                    WriteUInt32(b!, (uint)entry.Size);
                    WriteString(b!, entry.Name);
                }
                else
                {
                    byte[] nameBytes = Encoding.ASCII.GetBytes(entry.Name + '\0');
                    buffer!.Write(nameBytes, 0, nameBytes.Length);
                    buffer.Write(BitConverter.GetBytes((uint)entry.Offset), 0, 4);
                    buffer.Write(BitConverter.GetBytes((uint)entry.Size), 0, 4);
                    buffer.Write(BitConverter.GetBytes(0u), 0, 4);
                }
            }

            if (version == 6)
            {
                b!.Finish();
            }
            else
            {
                buffer!.WriteByte(0);
                buffer.WriteByte(0);
                buffer.WriteByte(0);
                buffer.WriteByte(0);

                long bufferSize = buffer.Length;
                buffer.Seek(0, SeekOrigin.Begin);
                ThLZSS.Compress(buffer, bufferSize, stream);
                buffer.Close();
            }

            stream.Seek(0, SeekOrigin.Begin);
            byte[] magicBytes = Encoding.ASCII.GetBytes(magic);
            stream.Write(magicBytes, 0, 4);

            if (version == 6)
            {
                b = new BitStream(stream);
                WriteUInt32(b, (uint)entries.Count);
                WriteUInt32(b, (uint)offset);
                b.Finish();
            }
            else
            {
                byte[] header = new byte[12];
                BitConverter.GetBytes((uint)entries.Count).CopyTo(header, 0);
                BitConverter.GetBytes((uint)offset).CopyTo(header, 4);
                BitConverter.GetBytes((uint)(buffer?.Length ?? 0)).CopyTo(header, 8);
                stream.Write(header, 0, 12);
            }

            return true;
        }

        public void AddEntry(string name)
        {
            entries.Add(new ThDatEntry { Name = name });
        }
    }
}
