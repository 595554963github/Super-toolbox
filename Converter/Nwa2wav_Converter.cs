using System.Text;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Nwa2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private class BitReader
        {
            private Stream _stream;
            private int _current;
            private int _bitPos;

            public BitReader(Stream stream)
            {
                _stream = stream;
                _current = 0;
                _bitPos = 8;
            }

            public int ReadBits(int n)
            {
                int bits = 0;
                int pos = 0;
                while (n > 0)
                {
                    var result = ReadAtMost(n);
                    bits |= (result.bits << pos);
                    pos += result.read;
                    n -= result.read;
                }
                return bits;
            }

            private (int read, int bits) ReadAtMost(int n)
            {
                if (_bitPos == 8)
                {
                    int b = _stream.ReadByte();
                    if (b == -1)
                        throw new EndOfStreamException();
                    _current = b;
                    _bitPos = 0;
                }

                int bits = _current;
                bits >>= _bitPos;
                bits &= (1 << n) - 1;
                int read = 8 - _bitPos;
                if (read > n)
                    read = n;
                _bitPos += read;
                return (read, bits);
            }
        }

        private class NwaHeader
        {
            public short Channels { get; private set; }
            public short Bps { get; private set; }
            public int Freq { get; private set; }
            public int Complevel { get; private set; }
            public int Userunlength { get; private set; }
            public int Blocks { get; private set; }
            public int Datasize { get; private set; }
            public int Compdatasize { get; private set; }
            public int Samplecount { get; private set; }
            public int Blocksize { get; private set; }
            public int Restsize { get; private set; }
            public int[] Offsets { get; private set; }

            public NwaHeader(Stream file)
            {
                byte[] headerBytes = new byte[44];
                int bytesRead = file.Read(headerBytes, 0, 44);
                if (bytesRead < 44)
                    throw new InvalidDataException("无效的NWA头,期望44字节,实际读取" + bytesRead + "字节");

                Channels = BitConverter.ToInt16(headerBytes, 0);
                Bps = BitConverter.ToInt16(headerBytes, 2);
                Freq = BitConverter.ToInt32(headerBytes, 4);
                Complevel = BitConverter.ToInt32(headerBytes, 8);
                Userunlength = BitConverter.ToInt32(headerBytes, 12);
                Blocks = BitConverter.ToInt32(headerBytes, 16);
                Datasize = BitConverter.ToInt32(headerBytes, 20);
                Compdatasize = BitConverter.ToInt32(headerBytes, 24);
                Samplecount = BitConverter.ToInt32(headerBytes, 28);
                Blocksize = BitConverter.ToInt32(headerBytes, 32);
                Restsize = BitConverter.ToInt32(headerBytes, 36);
                int dummy = BitConverter.ToInt32(headerBytes, 40);

                if (Complevel == -1)
                {
                    Blocksize = 65536;
                    int byps = Bps / 8;
                    if (byps > 0)
                    {
                        Restsize = (Datasize % (Blocksize * byps)) / byps;
                        int rest = Restsize > 0 ? 1 : 0;
                        Blocks = (Datasize / (Blocksize * byps)) + rest;
                    }
                }

                if (Blocks <= 0 || Blocks > 1000000)
                    throw new InvalidDataException("数据块数量异常:" + Blocks);

                Offsets = new int[Blocks];
                if (Complevel != -1)
                {
                    for (int i = 0; i < Blocks; i++)
                    {
                        byte[] offsetBytes = new byte[4];
                        int read = file.Read(offsetBytes, 0, 4);
                        if (read < 4)
                            throw new InvalidDataException("读取偏移量失败,索引:" + i);
                        Offsets[i] = BitConverter.ToInt32(offsetBytes, 0);
                    }
                }
            }

            public void Check()
            {
                if (Complevel != -1 && (Offsets == null || Offsets.Length == 0))
                    throw new InvalidDataException("需要偏移量但未设置");
                if (Channels != 1 && Channels != 2)
                    throw new InvalidDataException("仅支持单声道/双声道,当前通道数:" + Channels);
                if (Bps != 8 && Bps != 16)
                    throw new InvalidDataException("仅支持8位/16位音频,当前位深:" + Bps);

                int byps = Bps / 8;
                if (Complevel == -1)
                {
                    if (Datasize != Samplecount * byps)
                        throw new InvalidDataException("数据大小无效");
                    if (Samplecount != (Blocks - 1) * Blocksize + Restsize)
                        throw new InvalidDataException("采样数量无效");
                    return;
                }

                if (Complevel < -1 || Complevel > 5)
                    throw new InvalidDataException("不支持的压缩等级:" + Complevel);
                if (Blocks > 0 && Offsets[Blocks - 1] >= Compdatasize)
                    throw new InvalidDataException("最后一个偏移量超出文件范围");
                if (Datasize != Samplecount * byps)
                    throw new InvalidDataException("数据大小无效");
                if (Samplecount != (Blocks - 1) * Blocksize + Restsize)
                    throw new InvalidDataException("采样数量无效");
            }
        }

        private class NwaFile
        {
            private NwaHeader _header;
            private MemoryStream _dataStream;
            private int _curBlock;
            private BinaryWriter _dataWriter;

            public NwaFile(Stream file)
            {
                _header = new NwaHeader(file);
                _header.Check();

                int size = 44 + _header.Datasize;
                _dataStream = new MemoryStream(size);
                _dataWriter = new BinaryWriter(_dataStream);
                _curBlock = 0;

                WriteWaveHeader();

                int done = 0;
                while (done < _header.Datasize)
                {
                    done += DecodeBlock(file);
                }
            }

            private void WriteWaveHeader()
            {
                int byps = (_header.Bps + 7) >> 3;
                _dataWriter.Write(Encoding.ASCII.GetBytes("RIFF"));
                _dataWriter.Write(_header.Datasize + 36);
                _dataWriter.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                _dataWriter.Write(16);
                _dataWriter.Write((short)1);
                _dataWriter.Write(_header.Channels);
                _dataWriter.Write(_header.Freq);
                _dataWriter.Write(_header.Freq * _header.Channels * byps);
                _dataWriter.Write((short)(_header.Channels * byps));
                _dataWriter.Write(_header.Bps);
                _dataWriter.Write(Encoding.ASCII.GetBytes("data"));
                _dataWriter.Write(_header.Datasize);
            }

            public void Save(string filename)
            {
                using var fs = new FileStream(filename, FileMode.Create, FileAccess.Write);
                _dataStream.Seek(0, SeekOrigin.Begin);
                _dataStream.CopyTo(fs);
            }

            private int DecodeBlock(Stream file)
            {
                if (_header.Complevel == -1)
                {
                    _curBlock = _header.Blocks;
                    byte[] data = new byte[_header.Datasize];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int chunk = file.Read(data, read, data.Length - read);
                        if (chunk == 0) break;
                        read += chunk;
                    }
                    if (read > 0)
                        _dataWriter.Write(data, 0, read);
                    return read;
                }

                if (_header.Blocks == _curBlock)
                    return 0;

                int byps = _header.Bps / 8;
                int curBlocksize;
                int curCompsize;

                if (_curBlock != _header.Blocks - 1)
                {
                    curBlocksize = _header.Blocksize * byps;
                    curCompsize = _header.Offsets[_curBlock + 1] - _header.Offsets[_curBlock];
                }
                else
                {
                    curBlocksize = _header.Restsize * byps;
                    curCompsize = _header.Compdatasize - _header.Offsets[_curBlock];
                }

                if (curCompsize <= 0)
                    return 0;

                byte[] buf = new byte[curCompsize];
                int bytesRead = 0;
                while (bytesRead < buf.Length)
                {
                    int chunk = file.Read(buf, bytesRead, buf.Length - bytesRead);
                    if (chunk == 0) break;
                    bytesRead += chunk;
                }

                using var ms = new MemoryStream(buf);
                Decode(ms, curBlocksize);
                _curBlock++;
                return curBlocksize;
            }

            private void Decode(Stream buf, int outsize)
            {
                int[] d = new int[2];
                int flipflag = 0;
                int runlength = 0;

                if (_header.Bps == 8)
                {
                    int b = buf.ReadByte();
                    if (b == -1) throw new InvalidDataException();
                    d[0] = b - 128;
                }
                else
                {
                    byte[] sampleBytes = new byte[2];
                    int read = buf.Read(sampleBytes, 0, 2);
                    if (read < 2) throw new InvalidDataException();
                    d[0] = BitConverter.ToInt16(sampleBytes, 0);
                }

                if (_header.Channels == 2)
                {
                    if (_header.Bps == 8)
                    {
                        int b = buf.ReadByte();
                        if (b == -1) throw new InvalidDataException();
                        d[1] = b - 128;
                    }
                    else
                    {
                        byte[] sampleBytes = new byte[2];
                        int read = buf.Read(sampleBytes, 0, 2);
                        if (read < 2) throw new InvalidDataException();
                        d[1] = BitConverter.ToInt16(sampleBytes, 0);
                    }
                }

                var reader = new BitReader(buf);
                int byps = _header.Bps / 8;
                int dsize = outsize / byps;

                for (int i = 0; i < dsize; i++)
                {
                    if (runlength == 0)
                    {
                        int exponent = reader.ReadBits(3);
                        if (exponent == 7)
                        {
                            if (reader.ReadBits(1) == 1)
                            {
                                d[flipflag] = 0;
                            }
                            else
                            {
                                int bits = _header.Complevel >= 3 ? 8 : 8 - _header.Complevel;
                                int shift = _header.Complevel >= 3 ? 9 : 2 + 7 + _header.Complevel;
                                int mask1 = 1 << (bits - 1);
                                int mask2 = (1 << (bits - 1)) - 1;
                                int b = reader.ReadBits(bits);
                                if ((b & mask1) != 0)
                                    d[flipflag] -= (b & mask2) << shift;
                                else
                                    d[flipflag] += (b & mask2) << shift;
                            }
                        }
                        else if (exponent >= 1 && exponent <= 6)
                        {
                            int bitsCount = _header.Complevel >= 3 ? _header.Complevel + 3 : 5 - _header.Complevel;
                            int shift = _header.Complevel >= 3 ? 1 + exponent : 2 + exponent + _header.Complevel;
                            int mask1 = 1 << (bitsCount - 1);
                            int mask2 = (1 << (bitsCount - 1)) - 1;
                            int b = reader.ReadBits(bitsCount);
                            if ((b & mask1) != 0)
                                d[flipflag] -= (b & mask2) << shift;
                            else
                                d[flipflag] += (b & mask2) << shift;
                        }
                        else if (exponent == 0)
                        {
                            if (_header.Userunlength == 1)
                            {
                                runlength = reader.ReadBits(1);
                                if (runlength == 1)
                                {
                                    runlength = reader.ReadBits(2);
                                    if (runlength == 3)
                                    {
                                        runlength = reader.ReadBits(8);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        runlength--;
                    }

                    if (_header.Bps == 8)
                    {
                        _dataWriter.Write((byte)(d[flipflag] + 128));
                    }
                    else
                    {
                        _dataWriter.Write((short)d[flipflag]);
                    }

                    if (_header.Channels == 2)
                    {
                        flipflag ^= 1;
                    }
                }
            }
        }

        private class IndexEntry
        {
            public int Size { get; set; }
            public int Offset { get; set; }
            public int Count { get; set; }

            public IndexEntry(int size, int offset, int count)
            {
                Size = size;
                Offset = offset;
                Count = count;
            }
        }

        private List<IndexEntry> ReadIndex(string filePath, int headBlockSize)
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            byte[] indexCountBytes = new byte[4];
            int read = file.Read(indexCountBytes, 0, 4);
            if (read < 4)
                throw new InvalidDataException("读取索引数量失败");

            int indexcount = BitConverter.ToInt32(indexCountBytes, 0);

            if (indexcount <= 0)
                throw new InvalidDataException("索引数量无效:" + indexcount);

            var index = new List<IndexEntry>();
            for (int i = 0; i < indexcount; i++)
            {
                byte[] buf = new byte[headBlockSize];
                read = file.Read(buf, 0, headBlockSize);
                if (read < headBlockSize)
                    throw new InvalidDataException("读取索引项失败,索引:" + i);

                int size = BitConverter.ToInt32(buf, 0);
                int offset = BitConverter.ToInt32(buf, 4);
                int count = BitConverter.ToInt32(buf, 8);
                if (offset <= 0 || size <= 0)
                    throw new InvalidDataException("无效的索引项:偏移=" + offset + ",大小=" + size);
                index.Add(new IndexEntry(size, offset, count));
            }
            return index;
        }

        private bool ConvertNwa(string inputPath, string outputPath)
        {
            using var file = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
            var nwaFile = new NwaFile(file);
            nwaFile.Save(outputPath);
            return true;
        }

        private int ConvertNwk(string inputPath, string outputDir, string baseName)
        {
            var index = ReadIndex(inputPath, 12);
            int successCount = 0;
            foreach (var entry in index)
            {
                string outputPath = Path.Combine(outputDir, $"{baseName}-{entry.Count}.wav");
                using var file = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
                file.Seek(entry.Offset, SeekOrigin.Begin);
                var nwaFile = new NwaFile(file);
                nwaFile.Save(outputPath);
                successCount++;
                ConversionProgress?.Invoke(this, "  已解包:" + Path.GetFileName(outputPath));
            }
            return successCount;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, "源文件夹" + directoryPath + "不存在");
                OnConversionFailed("源文件夹" + directoryPath + "不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var nwaFiles = Directory.GetFiles(directoryPath, "*.nwa", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(directoryPath, "*.nwk", SearchOption.AllDirectories))
                        .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                        .ToArray();

                    TotalFilesToConvert = nwaFiles.Length;
                    int successCount = 0;

                    if (nwaFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的NWA/NWK文件");
                        OnConversionFailed("未找到需要转换的NWA/NWK文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, "开始转换,共" + TotalFilesToConvert + "个NWA/NWK文件");

                    foreach (var filePath in nwaFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string ext = Path.GetExtension(filePath).ToLower();
                        ConversionProgress?.Invoke(this, "正在转换:" + fileName + ext);

                        try
                        {
                            bool success = false;
                            if (ext == ".nwa")
                            {
                                string outputPath = Path.Combine(Path.GetDirectoryName(filePath) ?? "", $"{fileName}.wav");
                                success = ConvertNwa(filePath, outputPath);
                                if (success)
                                {
                                    successCount++;
                                    ConversionProgress?.Invoke(this, "已转换:" + fileName + ".wav");
                                    OnFileConverted(outputPath);
                                }
                            }
                            else if (ext == ".nwk")
                            {
                                string outputDir = Path.GetDirectoryName(filePath) ?? "";
                                int converted = ConvertNwk(filePath, outputDir, fileName);
                                successCount += converted;
                                success = converted > 0;
                            }

                            if (!success)
                            {
                                ConversionError?.Invoke(this, fileName + ext + "转换失败");
                                OnConversionFailed(fileName + ext + "转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, "转换异常:" + ex.Message);
                            OnConversionFailed(fileName + ext + "处理错误:" + ex.Message);
                        }
                    }

                    ConversionProgress?.Invoke(this, "转换完成,成功转换" + successCount + "/" + TotalFilesToConvert + "个文件");
                    OnConversionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "转换操作已取消");
                OnConversionFailed("转换操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, "转换失败:" + ex.Message);
                OnConversionFailed("转换失败:" + ex.Message);
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}