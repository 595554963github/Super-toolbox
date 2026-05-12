using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Asf2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static readonly short[] EaAdpcmTable = new short[]
        {
              0,  240,  460,  392,
              0,    0, -208, -220,
              0,    1,    3,    4,
              7,    8,   10,   11,
              0,   -1,   -3,   -4
        };

        private struct AsfBlockInfo
        {
            public string Tag;
            public uint Size;
        }

        private struct AsfStreamInfo
        {
            public int Revision;
            public int NumChannels;
            public int SampleRate;
            public uint TotalSamples;
            public uint NumBlocks;
        }

        private static int SignExtend(int val, int bits)
        {
            int shift = 32 - bits;
            return (val << shift) >> shift;
        }

        private static short ClipInt16(int val)
        {
            if (val > 32767) return 32767;
            if (val < -32768) return -32768;
            return (short)val;
        }

        private static ushort ReadLe16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static ushort ReadBe16(byte[] data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static uint ReadLe32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) |
                (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private static uint ReadBe32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static uint ReadArbitrary(byte[] data, ref int pos)
        {
            int size = data[pos++];
            uint word = 0;
            for (int i = 0; i < size; i++)
            {
                word = (word << 8) | data[pos++];
            }
            return word;
        }
        private static bool MatchTag(byte[] data, int offset, string tag)
        {
            byte[] tagBytes = System.Text.Encoding.ASCII.GetBytes(tag);
            for (int i = 0; i < 4; i++)
            {
                if (data[offset + i] != tagBytes[i]) return false;
            }
            return true;
        }

        private static AsfBlockInfo ParseBlockHeader(byte[] data, int offset)
        {
            var info = new AsfBlockInfo();
            info.Tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            info.Size = ReadLe32(data, offset + 4);
            return info;
        }

        private static AsfStreamInfo ParseSchlHeader(byte[] data, uint blockSize)
        {
            var info = new AsfStreamInfo();
            int end = (int)blockSize; 
            int p = 8;

            if (blockSize >= 16 && MatchTag(data, p, "GSTR"))
            {
                p += 8;
            }
            else if (blockSize >= 12 && data[p] == 'P' && data[p + 1] == 'T')
            {
                p += 4;
            }

            if (p + 3 <= end)
            {
                p += 3;
            }

            info.Revision = -1;
            info.NumChannels = 1;
            info.SampleRate = -1;
            info.TotalSamples = 0;

            bool inHeader = true;
            while (p < end && inHeader)
            {
                byte b = data[p++];

                switch (b)
                {
                    case 0xFD:
                        bool inSubheader = true;
                        while (p < end && inSubheader)
                        {
                            byte subbyte = data[p++];
                            switch (subbyte)
                            {
                                case 0x80:
                                    info.Revision = (int)ReadArbitrary(data, ref p);
                                    break;
                                case 0x82:
                                    info.NumChannels = (int)ReadArbitrary(data, ref p);
                                    break;
                                case 0x83:
                                    ReadArbitrary(data, ref p);
                                    break;
                                case 0x84:
                                    info.SampleRate = (int)ReadArbitrary(data, ref p);
                                    break;
                                case 0x85:
                                    info.TotalSamples = ReadArbitrary(data, ref p);
                                    break;
                                case 0x8A:
                                    ReadArbitrary(data, ref p);
                                    inSubheader = false;
                                    break;
                                case 0xFF:
                                    inSubheader = false;
                                    inHeader = false;
                                    break;
                                default:
                                    ReadArbitrary(data, ref p);
                                    break;
                            }
                        }
                        break;
                    case 0xFF:
                        inHeader = false;
                        break;
                    default:
                        ReadArbitrary(data, ref p);
                        break;
                }
            }

            if (info.Revision < 0)
            {
                info.Revision = 2;
            }

            if (info.SampleRate < 0)
            {
                info.SampleRate = (info.Revision == 3) ? 48000 : 22050;
            }

            return info;
        }

        private static uint ParseScclBlock(byte[] data, int offset, int revision)
        {
            if (revision == 3)
            {
                return ReadBe32(data, offset + 8);
            }
            else
            {
                return ReadLe32(data, offset + 8);
            }
        }

        private static void DecodeCompressedSubblock(byte[] data, int dataOffset, short[] samples, int samplesOffset, ref int s1, ref int s2)
        {
            byte header = data[dataOffset];
            int coeffIndex = header >> 4;
            int shift = 20 - (header & 0x0F);

            int coeff1 = EaAdpcmTable[coeffIndex];
            int coeff2 = EaAdpcmTable[coeffIndex + 4];

            int bytePtr = dataOffset;

            for (int i = 0; i < 28; i++)
            {
                int nibble;
                if ((i & 1) != 0)
                {
                    nibble = SignExtend(data[bytePtr] & 0x0F, 4);
                }
                else
                {
                    bytePtr++;
                    nibble = SignExtend(data[bytePtr] >> 4, 4);
                }

                int sample = (int)(nibble * (1 << shift)) + s1 * coeff1 + s2 * coeff2;
                sample = ClipInt16((sample + 0x80) >> 8);

                s2 = s1;
                s1 = sample;
                samples[samplesOffset + i] = (short)sample;
            }
        }

        private static void DecodeUncompressedSubblock(byte[] data, int dataOffset, short[] samples, int samplesOffset, ref int s1, ref int s2)
        {
            s1 = (short)ReadBe16(data, dataOffset + 1);
            s2 = (short)ReadBe16(data, dataOffset + 3);

            for (int i = 0; i < 28; i++)
            {
                samples[samplesOffset + i] = (short)ReadBe16(data, dataOffset + 5 + i * 2);
            }
        }

        private static int DecodeScdlBlock(byte[] blockData, int blockOffset, uint blockSize, int revision, int numChannels, int[,] channelState, bool isFirstBlock, short[] outputSamples)
        {
            int p = blockOffset + 8;

            uint numBlockSamples;
            if (revision == 3)
            {
                numBlockSamples = ReadBe32(blockData, p);
            }
            else
            {
                numBlockSamples = ReadLe32(blockData, p);
            }
            p += 4;

            uint[] channelOffsets = new uint[numChannels];
            for (int ch = 0; ch < numChannels; ch++)
            {
                if (revision == 3)
                {
                    channelOffsets[ch] = ReadBe32(blockData, p);
                }
                else
                {
                    channelOffsets[ch] = ReadLe32(blockData, p);
                }
                p += 4;
            }

            int channelDataStart = p;

            short[] channelSamples = new short[numBlockSamples];

            int nCompleteSubblocks = (int)(numBlockSamples / 28);
            int nExtraSamples = (int)(numBlockSamples % 28);

            for (int ch = 0; ch < numChannels; ch++)
            {
                int chData = channelDataStart + (int)channelOffsets[ch];
                int s1 = channelState[ch, 0];
                int s2 = channelState[ch, 1];

                if (revision == 1)
                {
                    s1 = (short)ReadLe16(blockData, chData);
                    s2 = (short)ReadLe16(blockData, chData + 2);
                    chData += 4;
                }

                int outOffset = 0;
                int subblockIdx = 0;

                if (isFirstBlock && revision > 1)
                {
                    int nUncompressed = Math.Min(3, nCompleteSubblocks);
                    for (int i = 0; i < nUncompressed; i++)
                    {
                        DecodeUncompressedSubblock(blockData, chData, channelSamples, outOffset, ref s1, ref s2);
                        chData += 61;
                        outOffset += 28;
                        subblockIdx++;
                    }
                }

                for (int i = subblockIdx; i < nCompleteSubblocks; i++)
                {
                    DecodeCompressedSubblock(blockData, chData, channelSamples, outOffset, ref s1, ref s2);
                    chData += 15;
                    outOffset += 28;
                }

                if (nExtraSamples > 0)
                {
                    short[] temp = new short[28];
                    if (revision == 1)
                    {
                        DecodeCompressedSubblock(blockData, chData, temp, 0, ref s1, ref s2);
                        chData += 15;
                    }
                    else
                    {
                        DecodeUncompressedSubblock(blockData, chData, temp, 0, ref s1, ref s2);
                        chData += 61;
                    }
                    Array.Copy(temp, 0, channelSamples, outOffset, nExtraSamples);
                }

                channelState[ch, 0] = s1;
                channelState[ch, 1] = s2;

                for (uint i = 0; i < numBlockSamples; i++)
                {
                    outputSamples[i * numChannels + ch] = channelSamples[i];
                }
            }

            return (int)numBlockSamples;
        }

        private static void WriteWavHeader(BinaryWriter bw, int numChannels, int sampleRate, uint numSamples)
        {
            uint dataSize = numSamples * (uint)numChannels * 2;
            uint byteRate = (uint)(sampleRate * numChannels * 2);
            ushort blockAlign = (ushort)(numChannels * 2);

            bw.Write('R');
            bw.Write('I');
            bw.Write('F');
            bw.Write('F');
            uint chunkSize = 36 + dataSize;
            bw.Write(chunkSize);
            bw.Write('W');
            bw.Write('A');
            bw.Write('V');
            bw.Write('E');

            bw.Write('f');
            bw.Write('m');
            bw.Write('t');
            bw.Write(' ');
            bw.Write(16);
            bw.Write((ushort)1);
            bw.Write((ushort)numChannels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write(blockAlign);
            bw.Write((ushort)16);

            bw.Write('d');
            bw.Write('a');
            bw.Write('t');
            bw.Write('a');
            bw.Write(dataSize);
        }

        private static bool ConvertAsfToWav(string inputPath, string outputPath)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(inputPath);
                int fileSize = fileData.Length;

                if (fileSize < 16)
                    return false;

                if (!MatchTag(fileData, 0, "SCHl"))
                    return false;

                AsfBlockInfo schlBlock = ParseBlockHeader(fileData, 0);
                AsfStreamInfo streamInfo = ParseSchlHeader(fileData, schlBlock.Size);

                if (streamInfo.Revision < 1 || streamInfo.Revision > 3 || streamInfo.NumChannels < 1)
                    return false;

                int offset = (int)schlBlock.Size;
                if (offset + 12 > fileSize)
                    return false;

                if (!MatchTag(fileData, offset, "SCCl"))
                    return false;

                AsfBlockInfo scclBlock = ParseBlockHeader(fileData, offset);
                streamInfo.NumBlocks = ParseScclBlock(fileData, offset, streamInfo.Revision);
                offset += (int)scclBlock.Size;

                using var fsOut = new FileStream(outputPath, FileMode.Create);
                using var bw = new BinaryWriter(fsOut);

                WriteWavHeader(bw, streamInfo.NumChannels, streamInfo.SampleRate, 0);

                int[,] channelState = new int[8, 2];
                bool isFirstBlock = true;
                uint totalDecodedSamples = 0;

                for (uint blockIdx = 0; blockIdx < streamInfo.NumBlocks; blockIdx++)
                {
                    if (offset + 8 > fileSize)
                        break;

                    if (!MatchTag(fileData, offset, "SCDl"))
                        break;

                    AsfBlockInfo scdlBlock = ParseBlockHeader(fileData, offset);
                    if (offset + scdlBlock.Size > fileSize)
                        break;

                    int maxSamples = (int)(((scdlBlock.Size - 8 - 4 - 4 * (uint)streamInfo.NumChannels)
                        / (uint)streamInfo.NumChannels / 15 + 1) * 28);
                    short[] blockBuffer = new short[maxSamples * streamInfo.NumChannels];

                    int decoded = DecodeScdlBlock(
                        fileData, offset, scdlBlock.Size,
                        streamInfo.Revision, streamInfo.NumChannels,
                        channelState, isFirstBlock,
                        blockBuffer
                    );

                    if (decoded > 0)
                    {
                        byte[] outBytes = new byte[decoded * streamInfo.NumChannels * 2];
                        Buffer.BlockCopy(blockBuffer, 0, outBytes, 0, outBytes.Length);
                        bw.Write(outBytes);
                        totalDecodedSamples += (uint)decoded;
                    }

                    isFirstBlock = false;
                    offset += (int)scdlBlock.Size;
                }

                bw.Flush();

                uint finalDataSize = totalDecodedSamples * (uint)streamInfo.NumChannels * 2;
                uint finalChunkSize = 36 + finalDataSize;
                
                fsOut.Seek(4, SeekOrigin.Begin);
                bw.Write(finalChunkSize);
                
                fsOut.Seek(40, SeekOrigin.Begin);
                bw.Write(finalDataSize);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var asfFiles = Directory.GetFiles(directoryPath, "*.asf", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"\d+");
                    if (match.Success && int.TryParse(match.Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = asfFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var asfFilePath in asfFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(asfFilePath);
                    ConversionProgress?.Invoke(this, $"正在转换:{Path.GetFileName(asfFilePath)}");

                    string fileDirectory = Path.GetDirectoryName(asfFilePath) ?? string.Empty;
                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavFilePath))
                            File.Delete(wavFilePath);

                        bool conversionSuccess = await Task.Run(() => ConvertAsfToWav(asfFilePath, wavFilePath), cancellationToken);

                        if (conversionSuccess && File.Exists(wavFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"已转换:{Path.GetFileName(wavFilePath)}");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{Path.GetFileName(asfFilePath)}转换失败");
                            OnConversionFailed($"{Path.GetFileName(asfFilePath)}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}处理错误:{ex.Message}");
                    }
                }

                ConversionProgress?.Invoke(this, successCount > 0
                    ? $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件"
                    : "转换完成,但未成功转换任何文件");

                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "转换操作已取消");
                OnConversionFailed("转换操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换失败:{ex.Message}");
                OnConversionFailed($"转换失败:{ex.Message}");
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}