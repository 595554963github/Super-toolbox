using Syroot.BinaryData.Extensions;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace super_toolbox
{
    public class CpkExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            string extractedRootDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedRootDir);

            var cpkFiles = Directory.GetFiles(directoryPath, "*.cpk", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedRootDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = cpkFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{cpkFiles.Count}个cpk文件");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var cpkFilePath in cpkFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string cpkFileName = Path.GetFileNameWithoutExtension(cpkFilePath);
                            string cpkExtractDir = Path.Combine(extractedRootDir, cpkFileName);
                            Directory.CreateDirectory(cpkExtractDir);

                            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(cpkFilePath)}");

                            using var arcView = new CpkMemoryMappedArcView(cpkFilePath);
                            var cpkOpener = new CpkArchiveOpener();
                            using var arcFile = cpkOpener.TryOpen(arcView);
                            if (arcFile == null)
                            {
                                ExtractionError?.Invoke(this, $"无法打开cpk文件:{Path.GetFileName(cpkFilePath)}");
                                continue;
                            }

                            int totalFiles = arcFile.Dir.Count;
                            int processedFiles = 0;

                            ExtractionProgress?.Invoke(this, $"cpk内包含{totalFiles}个文件");

                            foreach (var entry in arcFile.Dir)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                string outputPath = Path.Combine(cpkExtractDir, entry.Name);
                                string? outputDir = Path.GetDirectoryName(outputPath);
                                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                                    Directory.CreateDirectory(outputDir);

                                try
                                {
                                    using var stream = arcFile.OpenEntry(entry);

                                    var memoryStream = new MemoryStream();
                                    stream.CopyTo(memoryStream);
                                    memoryStream.Position = 0;

                                    byte[] fileData = memoryStream.ToArray();

                                    var phyreResult = TryExtractPhyreFile(fileData, out bool isPhyreFile);
                                    if (isPhyreFile)
                                    {
                                        string newName = Path.ChangeExtension(entry.Name, ".phyre");
                                        outputPath = Path.Combine(cpkExtractDir, newName);

                                        if (phyreResult != null)
                                        {
                                            fileData = phyreResult;
                                            ExtractionProgress?.Invoke(this, $"检测到phyre文件,已修复并重命名为:{newName}");
                                        }
                                        else
                                        {
                                            ExtractionProgress?.Invoke(this, $"检测到phyre文件但无法提取,重命名为:{newName}但保留原始数据");
                                        }
                                    }

                                    using var fileStream = File.Create(outputPath);
                                    fileStream.Write(fileData, 0, fileData.Length);

                                    processedFiles++;

                                    OnFileExtracted(outputPath);
                                    ExtractionProgress?.Invoke(this, $"已提取:{entry.Name} ({processedFiles}/{totalFiles})");
                                }
                                catch (Exception ex)
                                {
                                    ExtractionError?.Invoke(this, $"提取失败:{entry.Name} - {ex.Message}");
                                }
                            }

                            ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(cpkFilePath)} -> {processedFiles}/{totalFiles}个文件");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(cpkFilePath)}时出错:{ex.Message}");
                            OnExtractionFailed($"处理{Path.GetFileName(cpkFilePath)}时出错:{ex.Message}");
                        }
                    }
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }
        private byte[]? TryExtractPhyreFile(byte[] fileData, out bool isPhyreFile)
        {
            isPhyreFile = false;

            if (fileData.Length >= 4 &&
                fileData[0] == 0x52 && fileData[1] == 0x59 &&
                fileData[2] == 0x48 && fileData[3] == 0x50)
            {
                isPhyreFile = true;
                FixPhyreFile(fileData);
                return fileData;
            }

            byte[] phyrePattern = new byte[]
            {
                0x52, 0x59, 0x48, 0x50, 0x54, 0x00, 0x00, 0x00,
                0xD7, 0x08, 0x00, 0x00, 0x31, 0x31, 0x58, 0x44
            };

            int patternPosition = FindBytes(fileData, phyrePattern);

            if (patternPosition < 0)
            {
                return null;
            }

            isPhyreFile = true;

            if (patternPosition <= 100)
            {
                FixPhyreFile(fileData);
                return fileData;
            }
            else
            {
                int startPos = patternPosition;
                int remainingBytes = fileData.Length - startPos;

                if (startPos + 0x54 <= fileData.Length)
                {
                    int declaredSize = BitConverter.ToInt32(fileData, startPos + 0x50);

                    if (Math.Abs(remainingBytes - declaredSize) <= 256)
                    {
                        byte[] newData = new byte[remainingBytes];
                        Array.Copy(fileData, startPos, newData, 0, remainingBytes);
                        FixPhyreFile(newData);
                        return newData;
                    }
                }

                ExtractionProgress?.Invoke(this, $"检测到phyre文件,但大小解析不匹配,已跳过");
                return null;
            }
        }
        private void FixPhyreFile(byte[] fileData)
        {
            if (fileData.Length != 8296927) return;

            string hexData = "5259485054000000D7080000313158440200000003000000010000000A00000003000000000000000000000000000000020000001100000036000000000000000000000000000000000000000000000000907E0004030201D7080000050000000C000000290000001B030000000000000000000020000000000000000D000000280000000600000002000000030000302F00000003000000000000000000000000000000080000000000000000000000000000003F00000000000000000000000000000000000000200000000000000002000000000000404500000000000000000000000000000000000000020000000000000005000000540000205600000000000000000000000000000000000000000000000000000006000000540000207800000003000000000000000000000000000000000000000000000002000000480000206500000012000000000000000000000000000000000000000000000002000000240000208C0000000900000000000000000000000000000000000000000000000200000001000030A00000000100000000000000000000000000000000000000000000000B00000016000030C20000000000000000000000000000000000000000000000000000000900000016000030A80000000000000000000000000000000000000000000000000000000C00000016000030B3000000020000000000000000000000000000000000000000000000020000000E000030D2000000050000000000000000000000000000000000000000000000E50000000D00000000000000010000001000000000000000EA0000000700000001000000010000001600000000000000F20000000800000002000000010000001200000000000000FE000000000000004800000004000000000000000000000010010000000000004C0000000400000000000000000000002301000000000000500000000400000000000000000000003A01000000000000000000000400000008000000000000004801000000000000040000000400000008000000000000004F01000000000000100000000400000008000000000000006301000000000000080000000400000008000000000000007901000000000000140000000400000008000000000000008A01000000000000180000000400000008000000000000009C010000000000001C000000040000000800000000000000AF0100000000000020000000040000000800000000000000C30100000000000024000000040000000800000000000000DB0100000000000028000000040000000800000000000000F4010000000000002C0000000400000008000000000000000C02000000000000300000000400000008000000000000001D020000000000003400000004000000080000000000000031020000000000003800000004000000080000000000000041020000000000003C0000000400000008000000000000005C020000000000004000000004000000080000000000000074020000000000000C0000000400000008000000000000008102000000000000440000000400000008000000000000009302000000000000000000000400000008000000000000009D0200000000000004000000040000000800000000000000480100000000000008000000040000000800000000000000A5020000000000000C000000040000000800000000000000B30200000000000010000000040000000800000000000000F401000000000000140000000400000008000000000000008A0100000000000018000000040000000800000000000000AF010000000000001C000000040000000800000000000000DB0100000000000020000000040000000800000000000000C00200000100000000000000010000001000008000000000C9020000000000000E000000040000001000000000000000D10200000000000012000000040000001000000000000000DA0200000200000000000000010000001200000000000000E30200000300000001000000010000001000000000000000F00200000000000002000000040000001000000000000000FE02000000000000060000000400000010000000000000000C030000040000000A00000004000000100000000000000050436861720050496E743332005054657874757265466F726D617442617365005055496E743332005055496E7438005041737365745265666572656E63650050426173650050436C61737344657363726970746F720050436C75737465724865616465720050436C7573746572486561646572426173650050436C757374657248656164657244334431310050496E7374616E63654C6973744865616465720050537472696E67005054657874757265324400505465787475726532444261736500505465787475726532444433443131005054657874757265436F6D6D6F6E42617365006D5F6964006D5F6173736574006D5F617373657454797065006D5F696E64657842756666657253697A65006D5F76657274657842756666657253697A65006D5F6D61785465787475726542756666657253697A65006D5F70687972654D61726B6572006D5F73697A65006D5F696E7374616E63654C697374436F756E74006D5F7061636B65644E616D65737061636553697A65006D5F6172726179466978757053697A65006D5F61727261794669787570436F756E74006D5F706F696E746572466978757053697A65006D5F706F696E7465724669787570436F756E74006D5F706F696E7465724172726179466978757053697A65006D5F706F696E74657241727261794669787570436F756E74006D5F706F696E74657273496E417272617973436F756E74006D5F757365724669787570436F756E74006D5F7573657246697875704461746153697A65006D5F746F74616C4461746153697A65006D5F686561646572436C617373496E7374616E6365436F756E74006D5F686561646572436C6173734368696C64436F756E74006D5F706C6174666F726D4944006D5F70687973696373456E67696E654944006D5F636C6173734944006D5F636F756E74006D5F6F626A6563747353697A65006D5F61727261797353697A65006D5F627566666572006D5F7769647468006D5F686569676874006D5F666F726D6174006D5F6D656D6F727954797065006D5F6D69706D6170436F756E74006D5F6D61784D69704C6576656C006D5F74657874757265466C61677300010000000100000020000000040000001C000000000000000100000002000000000000000A0000000100000016000000160000000000000000000000000000000100000000";
            byte[] fixedData = HexToBytes(hexData);
            Array.Copy(fixedData, 0, fileData, 0x00, fixedData.Length);
        }

        private byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("\r", "").Replace("\n", "");
            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return result;
        }
        private int FindBytes(byte[] source, byte[] pattern)
        {
            if (source == null || pattern == null || pattern.Length == 0 || source.Length < pattern.Length)
                return -1;

            for (int i = 0; i <= source.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
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
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        internal class CpkArchiveOpener
        {
            public CpkArchiveFile? TryOpen(CpkMemoryMappedArcView view)
            {
                try
                {
                    var reader = new CpkIndexReader(view);
                    var dir = reader.ReadIndex();
                    if (dir == null || !dir.Any())
                        return null;

                    if (!reader.HasNames)
                        DetectFileTypes(view, dir);

                    return new CpkArchiveFile(view, dir);
                }
                catch
                {
                    return null;
                }
            }

            private void DetectFileTypes(CpkMemoryMappedArcView file, List<CpkEntry> dir)
            {
                foreach (var entry in dir)
                {
                    var offset = entry.Offset;
                    var signature = file.View.ReadUInt32(offset);
                    if (entry.Size > 0x10 && 0x4C495243 == signature)
                    {
                        uint packed_size = file.View.ReadUInt32(offset + 12);
                        if (packed_size < entry.Size - 0x10)
                        {
                            signature = file.View.ReadUInt32(offset + 0x10 + packed_size);
                            if (0x10 == signature)
                                signature = file.View.ReadUInt32(offset + 0x10 + packed_size + signature);
                        }
                    }
                }
            }
        }

        internal class CpkMemoryMappedArcView : IDisposable
        {
            private readonly FileStream _fileStream;
            private readonly MemoryMappedFile _mappedFile;
            private readonly MemoryMappedViewAccessor _viewAccessor;
            private bool _disposed = false;

            public CpkArcView View { get; }

            public CpkMemoryMappedArcView(string filePath)
            {
                _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                _mappedFile = MemoryMappedFile.CreateFromFile(_fileStream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
                _viewAccessor = _mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                View = new CpkArcView(_viewAccessor);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _viewAccessor.Dispose();
                _mappedFile.Dispose();
                _fileStream.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        internal class CpkArcView
        {
            private readonly MemoryMappedViewAccessor _accessor;
            private readonly byte[] _buffer = new byte[8];

            public CpkArcView(MemoryMappedViewAccessor accessor)
            {
                _accessor = accessor;
            }

            public bool AsciiEqual(long offset, string ascii)
            {
                for (int i = 0; i < ascii.Length; i++)
                {
                    if (_accessor.ReadByte(offset + i) != ascii[i])
                        return false;
                }
                return true;
            }

            public uint ReadUInt32(long offset)
            {
                _accessor.ReadArray(offset, _buffer, 0, 4);
                return (uint)(_buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16) | (_buffer[3] << 24));
            }

            public int ReadInt32(long offset)
            {
                _accessor.ReadArray(offset, _buffer, 0, 4);
                return _buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16) | (_buffer[3] << 24);
            }

            public long ReadInt64(long offset)
            {
                _accessor.ReadArray(offset, _buffer, 0, 8);
                long result = 0;
                for (int i = 0; i < 8; i++)
                {
                    result |= ((long)_buffer[i] << (i * 8));
                }
                return result;
            }

            public byte[] ReadBytes(long offset, uint count)
            {
                var data = new byte[count];
                _accessor.ReadArray(offset, data, 0, (int)count);
                return data;
            }

            public void Read(long offset, byte[] buffer, int index, int count)
            {
                _accessor.ReadArray(offset, buffer, index, count);
            }
        }

        internal class CpkArchiveFile : IDisposable
        {
            private readonly CpkMemoryMappedArcView _view;
            private readonly List<CpkEntry> _dir;
            private bool _disposed = false;

            public CpkMemoryMappedArcView File => _view;
            public List<CpkEntry> Dir => _dir;

            public CpkArchiveFile(CpkMemoryMappedArcView view, List<CpkEntry> dir)
            {
                _view = view;
                _dir = dir;
            }

            public Stream OpenEntry(CpkEntry entry)
            {
                if (entry.Size < 0x10 || !_view.View.AsciiEqual(entry.Offset, "CRILAYLA"))
                    return CreateStream(entry.Offset, entry.Size, entry.Name);

                var unpacked_size = _view.View.ReadInt32(entry.Offset + 8);
                var packed_size = _view.View.ReadUInt32(entry.Offset + 12);
                if (unpacked_size < 0 || packed_size > entry.Size - 0x10)
                    return CreateStream(entry.Offset, entry.Size, entry.Name);

                uint prefix_size = entry.Size - (0x10 + packed_size);
                var output = new byte[unpacked_size + prefix_size];
                var packed = _view.View.ReadBytes(entry.Offset + 0x10, packed_size);
                Array.Reverse(packed);

                using var mem = new MemoryStream(packed);
                using var input = new CpkMsbBitStream(mem);
                byte[] sizes = { 2, 3, 5, 8 };
                int dst = (int)prefix_size;
                while (dst < output.Length)
                {
                    if (0 == input.GetNextBit())
                    {
                        output[dst++] = (byte)input.GetBits(8);
                        continue;
                    }
                    int count = 3;
                    int offset = input.GetBits(13) + 3;
                    int rank = 0;
                    int bits, step;
                    do
                    {
                        bits = sizes[rank];
                        step = input.GetBits(bits);
                        count += step;
                        if (rank < 3)
                            rank++;
                    }
                    while (((1 << bits) - 1) == step);
                    BinaryCopyOverlapped(output, dst - offset, dst, count);
                    dst += count;
                }
                Array.Reverse(output, (int)prefix_size, unpacked_size);
                _view.View.Read(entry.Offset + 0x10 + packed_size, output, 0, (int)prefix_size);
                return new MemoryStream(output);
            }

            private Stream CreateStream(long offset, uint size, string name)
            {
                var data = _view.View.ReadBytes(offset, size);
                return new MemoryStream(data);
            }

            private void BinaryCopyOverlapped(byte[] data, int src, int dst, int count)
            {
                if (dst > src)
                {
                    while (count > 0)
                    {
                        int preceding = Math.Min(dst - src, count);
                        Buffer.BlockCopy(data, src, data, dst, preceding);
                        dst += preceding;
                        count -= preceding;
                    }
                }
                else
                {
                    Buffer.BlockCopy(data, src, data, dst, count);
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _view.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        internal class CpkIndexReader
        {
            private readonly CpkMemoryMappedArcView _file;
            private readonly CpkDeserializer _des = new CpkDeserializer();
            private long _content_offset;
            private readonly Dictionary<int, CpkEntry> _dir = new Dictionary<int, CpkEntry>();

            public bool HasNames { get; private set; }

            public CpkIndexReader(CpkMemoryMappedArcView file)
            {
                _file = file;
            }

            public List<CpkEntry>? ReadIndex()
            {
                try
                {
                    var chunk = ReadUTFChunk(4);
                    var header = _des.DeserializeUTFChunk(chunk).FirstOrDefault();
                    if (header == null) return null;

                    _content_offset = (long)header["ContentOffset"];
                    HasNames = header.ContainsKey("TocOffset");
                    if (HasNames)
                    {
                        ReadToc((long)header["TocOffset"]);
                    }
                    if (header.ContainsKey("ItocOffset"))
                    {
                        var align = (uint)(int)header["Align"];
                        ReadItoc((long)header["ItocOffset"], align);
                    }
                    return _dir.Values.ToList();
                }
                catch
                {
                    return null;
                }
            }

            private void ReadToc(long toc_offset)
            {
                var base_offset = Math.Min(_content_offset, toc_offset);
                if (!_file.View.AsciiEqual(toc_offset, "TOC "))
                    return;

                var chunk = ReadUTFChunk(toc_offset + 4);
                var table = _des.DeserializeUTFChunk(chunk);
                foreach (var row in table)
                {
                    var entry = new CpkEntry
                    {
                        Id = (int)row["ID"],
                        Offset = (long)row["FileOffset"] + base_offset,
                        Size = (uint)(int)row["FileSize"]
                    };
                    if (row.ContainsKey("ExtractSize"))
                        entry.UnpackedSize = (uint)(int)row["ExtractSize"];
                    else
                        entry.UnpackedSize = entry.Size;
                    entry.IsPacked = entry.Size != entry.UnpackedSize;
                    var name = (string)row["FileName"];
                    if (row.ContainsKey("DirName"))
                        name = Path.Combine((string)row["DirName"], name);
                    entry.Name = name ?? string.Empty;
                    _dir[entry.Id] = entry;
                }
            }

            private void ReadItoc(long toc_offset, uint align)
            {
                if (!_file.View.AsciiEqual(toc_offset, "ITOC"))
                    return;

                var chunk = ReadUTFChunk(toc_offset + 4);
                var itoc = _des.DeserializeUTFChunk(chunk).FirstOrDefault();
                if (null == itoc || !itoc.ContainsKey("DataL"))
                    return;

                var dataL = _des.DeserializeUTFChunk((byte[])itoc["DataL"]);
                var dataH = _des.DeserializeUTFChunk((byte[])itoc["DataH"]);
                foreach (var row in dataL.Concat(dataH))
                {
                    int id = (int)row["ID"];
                    var entry = GetEntryById(id);
                    entry.Size = (uint)(int)row["FileSize"];
                    if (row.ContainsKey("ExtractSize"))
                        entry.UnpackedSize = (uint)(int)row["ExtractSize"];
                    else
                        entry.UnpackedSize = entry.Size;
                    entry.IsPacked = entry.Size != entry.UnpackedSize;
                }
                long current_offset = _content_offset;
                foreach (var id in _dir.Keys.OrderBy(x => x))
                {
                    var entry = _dir[id];
                    entry.Offset = current_offset;
                    current_offset += entry.Size;
                    if (align != 0)
                    {
                        var tail = entry.Size % align;
                        if (tail > 0)
                            current_offset += align - tail;
                    }
                    if (string.IsNullOrEmpty(entry.Name))
                        entry.Name = id.ToString("D5");
                }
            }

            private CpkEntry GetEntryById(int id)
            {
                if (!_dir.TryGetValue(id, out var entry))
                {
                    entry = new CpkEntry { Id = id };
                    _dir[id] = entry;
                }
                return entry;
            }

            private byte[] ReadUTFChunk(long offset)
            {
                long chunk_size = _file.View.ReadInt64(offset + 4);
                if (chunk_size < 0 || chunk_size > int.MaxValue)
                    return new byte[0];

                var chunk = _file.View.ReadBytes(offset + 12, (uint)chunk_size);
                if (chunk.Length < chunk_size)
                    return new byte[0];

                if (!AsciiEqual(chunk, 0, "@UTF"))
                    DecryptUTFChunk(chunk);
                return chunk;
            }

            private bool AsciiEqual(byte[] chunk, int offset, string str)
            {
                for (int i = 0; i < str.Length; i++)
                {
                    if (chunk[offset + i] != str[i])
                        return false;
                }
                return true;
            }

            private void DecryptUTFChunk(byte[] chunk)
            {
                int key = 0x655F;
                for (int i = 0; i < chunk.Length; i++)
                {
                    chunk[i] ^= (byte)key;
                    key *= 0x4115;
                }
            }
        }

        internal class CpkDeserializer
        {
            private byte[] _chunk = Array.Empty<byte>();

            public List<Dictionary<string, object>> DeserializeUTFChunk(byte[] chunk)
            {
                _chunk = chunk;
                if (!AsciiEqual(_chunk, 0, "@UTF"))
                    return new List<Dictionary<string, object>>();

                var chunk_length = BigEndianToInt32(_chunk, 4);
                using var mem = new MemoryStream(_chunk, 8, chunk_length);
                int rows_offset = ReadInt32(mem);
                int strings_offset = ReadInt32(mem) + 8;
                int data_offset = ReadInt32(mem) + 8;
                mem.Seek(4, SeekOrigin.Current);
                int column_count = ReadInt16(mem);
                int row_length = ReadInt16(mem);
                int row_count = ReadInt32(mem);

                var columns = new List<CpkColumn>(column_count);
                for (int i = 0; i < column_count; ++i)
                {
                    byte flags = (byte)mem.ReadByte();
                    if (0 == flags)
                    {
                        mem.Seek(3, SeekOrigin.Current);
                        flags = (byte)mem.ReadByte();
                    }
                    int name_offset = strings_offset + ReadInt32(mem);
                    var column = new CpkColumn
                    {
                        Flags = (CpkTableFlags)flags,
                        Name = ReadString(name_offset)
                    };
                    columns.Add(column);
                }

                var table = new List<Dictionary<string, object>>(row_count);
                int next_offset = rows_offset;
                for (int i = 0; i < row_count; ++i)
                {
                    mem.Position = next_offset;
                    next_offset += row_length;
                    var row = new Dictionary<string, object>(column_count);
                    table.Add(row);
                    foreach (var column in columns)
                    {
                        var storage = column.Flags & CpkTableFlags.StorageMask;
                        if (CpkTableFlags.StorageNone == storage || CpkTableFlags.StorageZero == storage || CpkTableFlags.StorageConstant == storage)
                            continue;

                        switch (column.Flags & CpkTableFlags.TypeMask)
                        {
                            case CpkTableFlags.TypeByte:
                                row[column.Name] = (int)mem.ReadByte();
                                break;
                            case CpkTableFlags.TypeSByte:
                                row[column.Name] = (int)mem.ReadSByte();
                                break;
                            case CpkTableFlags.TypeUInt16:
                                row[column.Name] = (int)ReadUInt16(mem);
                                break;
                            case CpkTableFlags.TypeInt16:
                                row[column.Name] = (int)ReadInt16(mem);
                                break;
                            case CpkTableFlags.TypeUInt32:
                            case CpkTableFlags.TypeInt32:
                                row[column.Name] = ReadInt32(mem);
                                break;
                            case CpkTableFlags.TypeUInt64:
                            case CpkTableFlags.TypeInt64:
                                row[column.Name] = ReadInt64(mem);
                                break;
                            case CpkTableFlags.TypeString:
                                {
                                    int offset = strings_offset + ReadInt32(mem);
                                    row[column.Name] = ReadString(offset);
                                    break;
                                }
                            case CpkTableFlags.TypeData:
                                {
                                    int offset = data_offset + ReadInt32(mem);
                                    int length = ReadInt32(mem);
                                    row[column.Name] = _chunk.Skip(offset).Take(length).ToArray();
                                    break;
                                }
                            default:
                                break;
                        }
                    }
                }
                return table;
            }

            private bool AsciiEqual(byte[] data, int offset, string str)
            {
                for (int i = 0; i < str.Length; i++)
                {
                    if (data[offset + i] != str[i])
                        return false;
                }
                return true;
            }

            private string ReadString(int offset)
            {
                int length = 0;
                while (offset + length < _chunk.Length && _chunk[offset + length] != 0 && length < 0xFF)
                    length++;
                return Encoding.UTF8.GetString(_chunk, offset, length);
            }

            private int BigEndianToInt32(byte[] data, int offset)
            {
                return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            }

            private short ReadInt16(Stream stream)
            {
                int b1 = stream.ReadByte();
                int b2 = stream.ReadByte();
                if (b2 == -1) return 0;
                return (short)((b1 << 8) | b2);
            }

            private ushort ReadUInt16(Stream stream)
            {
                return (ushort)ReadInt16(stream);
            }

            private int ReadInt32(Stream stream)
            {
                int b1 = stream.ReadByte();
                int b2 = stream.ReadByte();
                int b3 = stream.ReadByte();
                int b4 = stream.ReadByte();
                if (b4 == -1) return 0;
                return (b1 << 24) | (b2 << 16) | (b3 << 8) | b4;
            }

            private long ReadInt64(Stream stream)
            {
                long high = (uint)ReadInt32(stream);
                long low = (uint)ReadInt32(stream);
                return (high << 32) | low;
            }
        }

        internal class CpkColumn
        {
            public CpkTableFlags Flags { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        [Flags]
        internal enum CpkTableFlags : byte
        {
            StorageMask = 0xF0,
            StorageNone = 0x00,
            StorageZero = 0x10,
            StorageConstant = 0x30,
            TypeMask = 0x0F,
            TypeByte = 0x00,
            TypeSByte = 0x01,
            TypeUInt16 = 0x02,
            TypeInt16 = 0x03,
            TypeUInt32 = 0x04,
            TypeInt32 = 0x05,
            TypeUInt64 = 0x06,
            TypeInt64 = 0x07,
            TypeFloat32 = 0x08,
            TypeString = 0x0A,
            TypeData = 0x0B,
        }

        internal class CpkMsbBitStream : IDisposable
        {
            private readonly Stream _input;
            private int _bits;
            private int _cached_bits;
            private bool _disposed = false;

            public CpkMsbBitStream(Stream input)
            {
                _input = input;
            }

            public int GetBits(int count)
            {
                while (_cached_bits < count)
                {
                    int b = _input.ReadByte();
                    if (-1 == b)
                        return -1;
                    _bits = (_bits << 8) | b;
                    _cached_bits += 8;
                }
                int mask = (1 << count) - 1;
                _cached_bits -= count;
                return (_bits >> _cached_bits) & mask;
            }

            public int GetNextBit()
            {
                return GetBits(1);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _input.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        internal class CpkEntry
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public long Offset { get; set; }
            public uint Size { get; set; }
            public uint UnpackedSize { get; set; }
            public bool IsPacked { get; set; }
        }
    }
}
