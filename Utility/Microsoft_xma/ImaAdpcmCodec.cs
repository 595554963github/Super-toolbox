using System;
using System.IO;
using System.Text;

namespace super_toolbox
{
    public class ImaAdpcmCodec
    {
        private const int NOISE_SHAPING_OFF = 0;
        private const int NOISE_SHAPING_STATIC = 0x100;
        private const int NOISE_SHAPING_DYNAMIC = 0x200;
        private const int LOOKAHEAD_DEPTH = 0x0ff;
        private const int LOOKAHEAD_EXHAUSTIVE = 0x800;
        private const int LOOKAHEAD_NO_BRANCHING = 0x400;

        private const int WAVE_FORMAT_PCM = 0x1;
        private const int WAVE_FORMAT_IMA_ADPCM = 0x11;

        private static readonly ushort[] StepTable = new ushort[89] {
            7, 8, 9, 10, 11, 12, 13, 14,
            16, 17, 19, 21, 23, 25, 28, 31,
            34, 37, 41, 45, 50, 55, 60, 66,
            73, 80, 88, 97, 107, 118, 130, 143,
            157, 173, 190, 209, 230, 253, 279, 307,
            337, 371, 408, 449, 494, 544, 598, 658,
            724, 796, 876, 963, 1060, 1166, 1282, 1411,
            1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
            3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
            7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
            32767
        };

        private static readonly int[] IndexTable = new int[] { -1, -1, -1, -1, 2, 4, 6, 8 };

        private class AdpcmChannel
        {
            public int PcmData;
            public int ShapingWeight;
            public int Error;
            public sbyte Index;
        }

        private class AdpcmContext
        {
            public AdpcmChannel[]? Channels;
            public int NumChannels;
            public int SampleRate;
            public int ConfigFlags;
            public short[]? DynamicShapingArray;
            public short LastShapingWeight;
            public int StaticShapingWeight;
        }

        private static int AdpcmSampleCountToBlockSize(int sampleCount, int numChannels, int bps)
        {
            return ((sampleCount - 1) * bps + 31) / 32 * numChannels * 4 + (numChannels * 4);
        }

        private static int AdpcmBlockSizeToSampleCount(int blockSize, int numChannels, int bps)
        {
            return (blockSize - numChannels * 4) / numChannels * 8 / bps + 1;
        }

        private static int AdpcmAlignBlockSize(int blockSize, int numChannels, int bps, bool roundUp)
        {
            int sampleCount = AdpcmBlockSizeToSampleCount(blockSize, numChannels, bps) - 1;
            int sampleAlign = (bps & 1) != 0 ? 32 : 32 / bps;

            if (roundUp)
                sampleCount = (sampleCount + sampleAlign - 1) / sampleAlign * sampleAlign;
            else
                sampleCount = sampleCount / sampleAlign * sampleAlign;

            return AdpcmSampleCountToBlockSize(sampleCount + 1, numChannels, bps);
        }

        private static AdpcmContext AdpcmCreateContext(int numChannels, int sampleRate, int lookahead, int noiseShaping)
        {
            AdpcmContext ctx = new AdpcmContext();
            ctx.Channels = new AdpcmChannel[2];
            ctx.Channels[0] = new AdpcmChannel();
            ctx.Channels[1] = new AdpcmChannel();
            ctx.ConfigFlags = noiseShaping | lookahead;
            ctx.StaticShapingWeight = 1024;
            ctx.NumChannels = numChannels;
            ctx.SampleRate = sampleRate;

            for (int ch = 0; ch < numChannels; ch++)
                ctx.Channels[ch].Index = -1;

            return ctx;
        }

        private static void AdpcmSetShapingWeight(AdpcmContext ctx, double shapingWeight)
        {
            ctx.StaticShapingWeight = (int)Math.Floor(shapingWeight * 1024.0 + 0.5);
            if (ctx.StaticShapingWeight > 1024) ctx.StaticShapingWeight = 1024;
            if (ctx.StaticShapingWeight < -1023) ctx.StaticShapingWeight = -1023;
        }

        private static int NoiseShape(AdpcmChannel ch, int sample)
        {
            int temp = -((ch.ShapingWeight * ch.Error + 512) >> 10);

            if (ch.ShapingWeight < 0 && temp != 0)
            {
                if (temp == ch.Error)
                    temp = (temp < 0) ? temp + 1 : temp - 1;

                ch.Error = -sample;
                sample += temp;
            }
            else
                ch.Error = -(sample += temp);

            return sample;
        }

        private static ulong MinError4Bit(AdpcmChannel ch, int nch, int csample, short[] psample, int psampleOffset, int flags, out int bestNibble, ulong maxError)
        {
            bestNibble = 0;
            AdpcmChannel chan = new AdpcmChannel { PcmData = ch.PcmData, Index = ch.Index, ShapingWeight = ch.ShapingWeight, Error = ch.Error };
            ushort step = StepTable[chan.Index];
            ushort trialDelta = (ushort)(step >> 3);

            int delta = csample - ch.PcmData;
            int nibble;
            if (delta < 0)
            {
                int mag = ((-delta << 2) + (step & 3) + ((step & 1) << 1)) / step;
                nibble = 0x8 | (mag > 7 ? 7 : mag);
            }
            else
            {
                int mag = ((delta << 2) + (step & 3) + ((step & 1) << 1)) / step;
                nibble = mag > 7 ? 7 : mag;
            }

            if ((nibble & 1) != 0) trialDelta += (ushort)(step >> 2);
            if ((nibble & 2) != 0) trialDelta += (ushort)(step >> 1);
            if ((nibble & 4) != 0) trialDelta += step;

            if ((nibble & 8) != 0)
                chan.PcmData -= trialDelta;
            else
                chan.PcmData += trialDelta;

            if (chan.PcmData > 32767) chan.PcmData = 32767;
            if (chan.PcmData < -32768) chan.PcmData = -32768;
            bestNibble = nibble;
            ulong minError = (ulong)(chan.PcmData - csample) * (ulong)(chan.PcmData - csample);

            if ((flags & LOOKAHEAD_DEPTH) == 0 || minError >= maxError)
                return minError;

            chan.Index = (sbyte)(chan.Index + IndexTable[nibble & 0x07]);
            if (chan.Index < 0) chan.Index = 0;
            if (chan.Index > 88) chan.Index = 88;

            int csample2;
            if ((flags & (NOISE_SHAPING_STATIC | NOISE_SHAPING_DYNAMIC)) != 0)
            {
                chan.Error += chan.PcmData;
                csample2 = NoiseShape(chan, psample[psampleOffset + nch]);
            }
            else
                csample2 = psample[psampleOffset + nch];

            int tempBest;
            minError += MinError4Bit(chan, nch, csample2, psample, psampleOffset + nch, flags - 1, out tempBest, maxError - minError);

            if ((flags & LOOKAHEAD_NO_BRANCHING) != 0)
                return minError;

            for (int testNibble = 0; testNibble <= 0xF; testNibble++)
            {
                if (testNibble == nibble) continue;

                if ((flags & LOOKAHEAD_EXHAUSTIVE) != 0 || (testNibble & ~0x7) == 0 || Math.Abs(testNibble - nibble) <= 3)
                {
                    trialDelta = (ushort)(step >> 3);
                    chan = new AdpcmChannel { PcmData = ch.PcmData, Index = ch.Index, ShapingWeight = ch.ShapingWeight, Error = ch.Error };

                    if ((testNibble & 1) != 0) trialDelta += (ushort)(step >> 2);
                    if ((testNibble & 2) != 0) trialDelta += (ushort)(step >> 1);
                    if ((testNibble & 4) != 0) trialDelta += step;

                    if ((testNibble & 8) != 0)
                        chan.PcmData -= trialDelta;
                    else
                        chan.PcmData += trialDelta;

                    if (chan.PcmData > 32767) chan.PcmData = 32767;
                    if (chan.PcmData < -32768) chan.PcmData = -32768;

                    ulong error = (ulong)(chan.PcmData - csample) * (ulong)(chan.PcmData - csample);
                    ulong threshold = maxError < minError ? maxError : minError;

                    if (error < threshold)
                    {
                        chan.Index = (sbyte)(chan.Index + IndexTable[testNibble & 0x07]);
                        if (chan.Index < 0) chan.Index = 0;
                        if (chan.Index > 88) chan.Index = 88;

                        if ((flags & (NOISE_SHAPING_STATIC | NOISE_SHAPING_DYNAMIC)) != 0)
                        {
                            chan.Error += chan.PcmData;
                            csample2 = NoiseShape(chan, psample[psampleOffset + nch]);
                        }
                        else
                            csample2 = psample[psampleOffset + nch];

                        error += MinError4Bit(chan, nch, csample2, psample, psampleOffset + nch, flags - 1, out tempBest, threshold - error);

                        if (error < minError)
                        {
                            bestNibble = testNibble;
                            minError = error;
                        }
                    }
                }
            }

            return minError;
        }

        private static byte EncodeSample(AdpcmContext ctx, int ch, int bps, short[] psample, int psampleOffset, int numSamples)
        {
            AdpcmChannel channel = ctx.Channels![ch];
            ushort step = StepTable[channel.Index];
            int flags = ctx.ConfigFlags;
            int csample = psample[psampleOffset];

            if ((flags & (NOISE_SHAPING_STATIC | NOISE_SHAPING_DYNAMIC)) != 0)
                csample = NoiseShape(channel, csample);

            int lookaheadDepth = flags & LOOKAHEAD_DEPTH;
            if (lookaheadDepth > numSamples - 1)
                flags = (flags & ~LOOKAHEAD_DEPTH) + (numSamples - 1);

            int nibble;
            ushort trialDelta;
            MinError4Bit(channel, ctx.NumChannels, csample, psample, psampleOffset, flags, out nibble, ulong.MaxValue);

            trialDelta = (ushort)(step >> 3);
            if ((nibble & 1) != 0) trialDelta += (ushort)(step >> 2);
            if ((nibble & 2) != 0) trialDelta += (ushort)(step >> 1);
            if ((nibble & 4) != 0) trialDelta += step;

            if ((nibble & 8) != 0)
                channel.PcmData -= trialDelta;
            else
                channel.PcmData += trialDelta;

            channel.Index = (sbyte)(channel.Index + IndexTable[nibble & 0x07]);
            if (channel.Index < 0) channel.Index = 0;
            if (channel.Index > 88) channel.Index = 88;
            if (channel.PcmData > 32767) channel.PcmData = 32767;
            if (channel.PcmData < -32768) channel.PcmData = -32768;

            if ((flags & (NOISE_SHAPING_STATIC | NOISE_SHAPING_DYNAMIC)) != 0)
                channel.Error += channel.PcmData;

            return (byte)nibble;
        }

        private static void EncodeChunks(AdpcmContext ctx, byte[] outbuf, ref int outbufOffset, short[] inbuf, int inbufOffset, int inbufCount, int bps)
        {
            for (int ch = 0; ch < ctx.NumChannels; ch++)
            {
                int shiftBits = 0;
                int numBits = 0;
                int j = 0;

                if ((ctx.ConfigFlags & NOISE_SHAPING_STATIC) != 0)
                    ctx.Channels![ch].ShapingWeight = ctx.StaticShapingWeight;

                int pcmOffset = inbufOffset + ch;
                for (int i = 0; i < inbufCount; i++)
                {
                    if ((ctx.ConfigFlags & NOISE_SHAPING_DYNAMIC) != 0)
                        ctx.Channels![ch].ShapingWeight = ctx.DynamicShapingArray![i];

                    shiftBits |= EncodeSample(ctx, ch, bps, inbuf, pcmOffset, inbufCount - i) << numBits;
                    pcmOffset += ctx.NumChannels;

                    numBits += bps;
                    if (numBits >= 8)
                    {
                        outbuf[outbufOffset + ((j & ~3) * ctx.NumChannels + (ch * 4) + (j & 3))] = (byte)shiftBits;
                        shiftBits >>= 8;
                        numBits -= 8;
                        j++;
                    }
                }

                if (numBits != 0)
                    outbuf[outbufOffset + ((j & ~3) * ctx.NumChannels + (ch * 4) + (j & 3))] = (byte)shiftBits;
            }

            outbufOffset += ((inbufCount * bps + 31) / 32 * ctx.NumChannels * 4);
        }

        private static bool AdpcmEncodeBlockEx(AdpcmContext ctx, byte[] outbuf, out int outbufSize, short[] inbuf, int inbufOffset, int inbufCount, int bps)
        {
            outbufSize = 0;
            if (bps < 2 || bps > 5) return false;
            if (inbufCount == 0) return true;

            int offset = 0;
            for (int ch = 0; ch < ctx.NumChannels; ch++)
                ctx.Channels![ch].PcmData = inbuf[inbufOffset + ch];
            inbufCount--;
            inbufOffset += ctx.NumChannels;

            if (inbufCount > 0 && (ctx.Channels![0].Index < 0 || (ctx.ConfigFlags & LOOKAHEAD_DEPTH) >= 3))
            {
                int flags = 16 | LOOKAHEAD_NO_BRANCHING;
                int depth = flags & LOOKAHEAD_DEPTH;
                if (depth > inbufCount - 1)
                    flags = (flags & ~LOOKAHEAD_DEPTH) + (inbufCount - 1);

                for (int ch = 0; ch < ctx.NumChannels; ch++)
                {
                    ulong minError = ulong.MaxValue;
                    int bestIndex = 0;

                    for (int tIndex = 0; tIndex <= 88; tIndex++)
                    {
                        AdpcmChannel chan = new AdpcmChannel { PcmData = ctx.Channels![ch].PcmData, Index = (sbyte)tIndex, ShapingWeight = 0, Error = 0 };
                        int tempBest;
                        MinError4Bit(chan, ctx.NumChannels, inbuf[inbufOffset + ch], inbuf, inbufOffset + ch, flags, out tempBest, ulong.MaxValue);
                    }

                    for (int tIndex = 0; tIndex <= 87; tIndex++)
                    {
                        AdpcmChannel chan = new AdpcmChannel { PcmData = ctx.Channels![ch].PcmData, Index = (sbyte)tIndex, ShapingWeight = 0, Error = 0 };
                        int tempBest;
                        ulong error = MinError4Bit(chan, ctx.NumChannels, inbuf[inbufOffset + ch], inbuf, inbufOffset + ch, flags, out tempBest, ulong.MaxValue);
                        ulong terror = error;
                        if (tIndex > 0)
                        {
                            AdpcmChannel chanLeft = new AdpcmChannel { PcmData = ctx.Channels![ch].PcmData, Index = (sbyte)(tIndex - 1), ShapingWeight = 0, Error = 0 };
                            AdpcmChannel chanRight = new AdpcmChannel { PcmData = ctx.Channels![ch].PcmData, Index = (sbyte)(tIndex + 1), ShapingWeight = 0, Error = 0 };
                            ulong errorLeft = MinError4Bit(chanLeft, ctx.NumChannels, inbuf[inbufOffset + ch], inbuf, inbufOffset + ch, flags, out tempBest, ulong.MaxValue);
                            ulong errorRight = MinError4Bit(chanRight, ctx.NumChannels, inbuf[inbufOffset + ch], inbuf, inbufOffset + ch, flags, out tempBest, ulong.MaxValue);
                            terror = (errorLeft + error + errorRight) / 3;
                        }

                        if (terror < minError)
                        {
                            bestIndex = tIndex;
                            minError = terror;
                        }
                    }

                    ctx.Channels![ch].Index = (sbyte)bestIndex;
                }
            }

            for (int ch = 0; ch < ctx.NumChannels; ch++)
            {
                outbuf[offset++] = (byte)(ctx.Channels![ch].PcmData & 0xFF);
                outbuf[offset++] = (byte)((ctx.Channels![ch].PcmData >> 8) & 0xFF);
                outbuf[offset++] = (byte)ctx.Channels![ch].Index;
                outbuf[offset++] = 0;
                outbufSize += 4;
            }

            if (inbufCount > 0 && (ctx.ConfigFlags & NOISE_SHAPING_DYNAMIC) != 0)
            {
                ctx.DynamicShapingArray = new short[inbufCount];
                for (int i = 0; i < inbufCount; i++)
                    ctx.DynamicShapingArray[i] = 0;
                ctx.LastShapingWeight = 0;
            }

            if (inbufCount > 0)
                EncodeChunks(ctx, outbuf, ref offset, inbuf, inbufOffset, inbufCount, bps);

            outbufSize = offset;
            return true;
        }

        private static int AdpcmDecodeBlock(short[] outbuf, int outbufOffset, byte[] inbuf, int inbufOffset, int inbufSize, int channels)
        {
            int samples = 1;
            int[] pcmData = new int[2];
            sbyte[] index = new sbyte[2];

            if (inbufSize < channels * 4)
                return 0;

            int offset = inbufOffset;
            for (int ch = 0; ch < channels; ch++)
            {
                pcmData[ch] = (short)(inbuf[offset] | (inbuf[offset + 1] << 8));
                outbuf[outbufOffset + ch] = (short)pcmData[ch];
                index[ch] = (sbyte)inbuf[offset + 2];

                if (index[ch] < 0 || index[ch] > 88 || inbuf[offset + 3] != 0)
                    return 0;

                offset += 4;
            }

            inbufSize -= channels * 4;
            int chunks = inbufSize / (channels * 4);
            samples += chunks * 8;
            int outOffset = outbufOffset + channels;

            while (chunks-- > 0)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        ushort step = StepTable[index[ch]];
                        ushort delta = (ushort)(step >> 3);
                        byte nibble = inbuf[offset++];

                        if ((nibble & 1) != 0) delta += (ushort)(step >> 2);
                        if ((nibble & 2) != 0) delta += (ushort)(step >> 1);
                        if ((nibble & 4) != 0) delta += step;

                        if ((nibble & 8) != 0)
                            pcmData[ch] -= delta;
                        else
                            pcmData[ch] += delta;

                        index[ch] = (sbyte)(index[ch] + IndexTable[nibble & 0x07]);
                        if (index[ch] < 0) index[ch] = 0;
                        if (index[ch] > 88) index[ch] = 88;
                        if (pcmData[ch] > 32767) pcmData[ch] = 32767;
                        if (pcmData[ch] < -32768) pcmData[ch] = -32768;
                        outbuf[outOffset + i * 2 * channels + ch] = (short)pcmData[ch];

                        step = StepTable[index[ch]];
                        delta = (ushort)(step >> 3);

                        if ((nibble & 0x10) != 0) delta += (ushort)(step >> 2);
                        if ((nibble & 0x20) != 0) delta += (ushort)(step >> 1);
                        if ((nibble & 0x40) != 0) delta += step;

                        if ((nibble & 0x80) != 0)
                            pcmData[ch] -= delta;
                        else
                            pcmData[ch] += delta;

                        index[ch] = (sbyte)(index[ch] + IndexTable[(nibble >> 4) & 0x07]);
                        if (index[ch] < 0) index[ch] = 0;
                        if (index[ch] > 88) index[ch] = 88;
                        if (pcmData[ch] > 32767) pcmData[ch] = 32767;
                        if (pcmData[ch] < -32768) pcmData[ch] = -32768;
                        outbuf[outOffset + (i * 2 + 1) * channels + ch] = (short)pcmData[ch];
                    }
                }
                outOffset += channels * 8;
            }

            return samples;
        }

        private static int ReadUInt16(BinaryReader br)
        {
            return br.ReadByte() | (br.ReadByte() << 8);
        }

        private static int ReadUInt32(BinaryReader br)
        {
            int b0 = br.ReadByte();
            int b1 = br.ReadByte();
            int b2 = br.ReadByte();
            int b3 = br.ReadByte();
            return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }

        private static void WriteUInt16(BinaryWriter bw, int value)
        {
            bw.Write((byte)(value & 0xFF));
            bw.Write((byte)((value >> 8) & 0xFF));
        }

        private static void WriteUInt32(BinaryWriter bw, int value)
        {
            bw.Write((byte)(value & 0xFF));
            bw.Write((byte)((value >> 8) & 0xFF));
            bw.Write((byte)((value >> 16) & 0xFF));
            bw.Write((byte)((value >> 24) & 0xFF));
        }

        public static int EncodeFile(string inputFile, string outputFile, int flags, int lookahead, int blocksizePow2, int encodeWidthBits, double staticShapingWeight)
        {
            int numChannels = 0;
            uint numSamples = 0;
            uint sampleRate = 0;
            int blockAlign = 0;

            using (FileStream fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                string riff = new string(br.ReadChars(4));
                uint riffSize = (uint)ReadUInt32(br);
                string wave = new string(br.ReadChars(4));

                if (riff != "RIFF" || wave != "WAVE")
                    return -1;

                while (true)
                {
                    string chunkId = new string(br.ReadChars(4));
                    uint chunkSize = (uint)ReadUInt32(br);

                    if (chunkId == "fmt ")
                    {
                        ushort formatTag = (ushort)ReadUInt16(br);
                        numChannels = ReadUInt16(br);
                        sampleRate = (uint)ReadUInt32(br);
                        uint bytesPerSecond = (uint)ReadUInt32(br);
                        blockAlign = ReadUInt16(br);
                        ushort bitsPerSample = (ushort)ReadUInt16(br);

                        if (formatTag != WAVE_FORMAT_PCM)
                            return -1;

                        if (chunkSize > 16)
                        {
                            int extra = (int)chunkSize - 16;
                            if (extra > 0)
                                br.ReadBytes(extra);
                        }
                    }
                    else if (chunkId == "fact")
                    {
                        numSamples = (uint)ReadUInt32(br);
                        if (chunkSize > 4)
                            br.ReadBytes((int)chunkSize - 4);
                    }
                    else if (chunkId == "data")
                    {
                        numSamples = chunkSize / (uint)(numChannels * 2);
                        break;
                    }
                    else
                    {
                        int bytesToSkip = (int)chunkSize;
                        if ((bytesToSkip & 1) != 0)
                            bytesToSkip++;
                        br.ReadBytes(bytesToSkip);
                    }
                }

                long dataStartPos = fs.Position;

                int blockSize;
                if (blocksizePow2 != 0)
                    blockSize = 1 << blocksizePow2;
                else
                    blockSize = 256 * numChannels * (sampleRate < 11000 ? 1 : (int)(sampleRate / 11000));

                int tempBlockSize = blockSize;
                while ((tempBlockSize & (tempBlockSize - 1)) != 0)
                    tempBlockSize++;
                blockSize = tempBlockSize;

                blockSize = AdpcmAlignBlockSize(blockSize, numChannels, encodeWidthBits, false);
                int samplesPerBlock = AdpcmBlockSizeToSampleCount(blockSize, numChannels, encodeWidthBits);

                using (FileStream fsOut = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                using (BinaryWriter bw = new BinaryWriter(fsOut))
                {
                    WriteAdpcmWavHeader(bw, numChannels, encodeWidthBits, numSamples, sampleRate, samplesPerBlock);

                    fs.Position = dataStartPos;

                    AdpcmContext ctx = AdpcmCreateContext(numChannels, (int)sampleRate, lookahead, NOISE_SHAPING_STATIC);
                    AdpcmSetShapingWeight(ctx, staticShapingWeight);

                    uint remainingSamples = numSamples;
                    short[] pcmBlock = new short[samplesPerBlock * numChannels];
                    byte[] adpcmBlock = new byte[blockSize];

                    while (remainingSamples > 0)
                    {
                        int thisBlockPcmSamples = samplesPerBlock;
                        if (thisBlockPcmSamples > (int)remainingSamples)
                            thisBlockPcmSamples = (int)remainingSamples;

                        for (int i = 0; i < thisBlockPcmSamples * numChannels; i++)
                        {
                            int sample = br.ReadByte();
                            sample |= br.ReadByte() << 8;
                            pcmBlock[i] = (short)sample;
                        }

                        int finalBlockSize = blockSize;
                        int thisBlockAdpcmSamples = samplesPerBlock;
                        if (thisBlockPcmSamples < samplesPerBlock)
                        {
                            finalBlockSize = AdpcmAlignBlockSize(AdpcmSampleCountToBlockSize(thisBlockPcmSamples, numChannels, encodeWidthBits), numChannels, encodeWidthBits, true);
                            thisBlockAdpcmSamples = AdpcmBlockSizeToSampleCount(finalBlockSize, numChannels, encodeWidthBits);

                            for (int i = thisBlockPcmSamples * numChannels; i < thisBlockAdpcmSamples * numChannels; i++)
                                pcmBlock[i] = pcmBlock[thisBlockPcmSamples * numChannels - numChannels + (i - thisBlockPcmSamples * numChannels) % numChannels];
                        }

                        int outbufSize;
                        AdpcmEncodeBlockEx(ctx, adpcmBlock, out outbufSize, pcmBlock, 0, thisBlockAdpcmSamples, encodeWidthBits);
                        bw.Write(adpcmBlock, 0, finalBlockSize);

                        remainingSamples -= (uint)thisBlockPcmSamples;
                    }
                }
            }

            return 0;
        }

        public static int DecodeFile(string inputFile, string outputFile, int flags)
        {
            int numChannels = 0;
            int bitsPerSample = 0;
            uint numSamples = 0;
            uint sampleRate = 0;
            int blockAlign = 0;
            int samplesPerBlock = 0;

            using (FileStream fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                string riff = new string(br.ReadChars(4));
                uint riffSize = (uint)ReadUInt32(br);
                string wave = new string(br.ReadChars(4));

                if (riff != "RIFF" || wave != "WAVE")
                    return -1;

                while (true)
                {
                    string chunkId = new string(br.ReadChars(4));
                    uint chunkSize = (uint)ReadUInt32(br);

                    if (chunkId == "fmt ")
                    {
                        ushort formatTag = (ushort)ReadUInt16(br);
                        numChannels = ReadUInt16(br);
                        sampleRate = (uint)ReadUInt32(br);
                        uint bytesPerSecond = (uint)ReadUInt32(br);
                        blockAlign = ReadUInt16(br);
                        bitsPerSample = ReadUInt16(br);

                        if (formatTag != WAVE_FORMAT_IMA_ADPCM)
                            return -1;

                        if (chunkSize > 16)
                        {
                            ushort cbSize = (ushort)ReadUInt16(br);
                            if (cbSize >= 2)
                                samplesPerBlock = ReadUInt16(br);
                            int extra = (int)chunkSize - 20;
                            if (extra > 0)
                                br.ReadBytes(extra);
                        }
                    }
                    else if (chunkId == "fact")
                    {
                        numSamples = (uint)ReadUInt32(br);
                        if (chunkSize > 4)
                            br.ReadBytes((int)chunkSize - 4);
                    }
                    else if (chunkId == "data")
                    {
                        if (numSamples == 0 && samplesPerBlock > 0)
                            numSamples = chunkSize / (uint)blockAlign * (uint)samplesPerBlock;
                        break;
                    }
                    else
                    {
                        int bytesToSkip = (int)chunkSize;
                        if ((bytesToSkip & 1) != 0)
                            bytesToSkip++;
                        br.ReadBytes(bytesToSkip);
                    }
                }

                long dataStartPos = fs.Position;

                if (samplesPerBlock == 0)
                    samplesPerBlock = AdpcmBlockSizeToSampleCount(blockAlign, numChannels, bitsPerSample);

                using (FileStream fsOut = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                using (BinaryWriter bw = new BinaryWriter(fsOut))
                {
                    WritePcmWavHeader(bw, numChannels, numSamples, sampleRate);

                    fs.Position = dataStartPos;

                    uint remainingSamples = numSamples;
                    short[] pcmBlock = new short[samplesPerBlock * numChannels];
                    byte[] adpcmBlock = new byte[blockAlign];

                    while (remainingSamples > 0)
                    {
                        int thisBlockPcmSamples = samplesPerBlock;
                        int currentBlockAlign = blockAlign;
                        if (thisBlockPcmSamples > (int)remainingSamples)
                        {
                            thisBlockPcmSamples = (int)remainingSamples;
                            currentBlockAlign = AdpcmSampleCountToBlockSize(thisBlockPcmSamples, numChannels, bitsPerSample);
                        }

                        br.Read(adpcmBlock, 0, currentBlockAlign);
                        int decodedSamples = AdpcmDecodeBlock(pcmBlock, 0, adpcmBlock, 0, currentBlockAlign, numChannels);

                        for (int i = 0; i < thisBlockPcmSamples * numChannels; i++)
                        {
                            short sample = pcmBlock[i];
                            bw.Write((byte)(sample & 0xFF));
                            bw.Write((byte)((sample >> 8) & 0xFF));
                        }

                        remainingSamples -= (uint)thisBlockPcmSamples;
                    }
                }
            }

            return 0;
        }

        private static void WritePcmWavHeader(BinaryWriter bw, int numChannels, uint numSamples, uint sampleRate)
        {
            int bytesPerSample = 2;
            uint totalDataBytes = numSamples * (uint)bytesPerSample * (uint)numChannels;

            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            WriteUInt32(bw, 36 + (int)totalDataBytes);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            WriteUInt32(bw, 16);
            WriteUInt16(bw, WAVE_FORMAT_PCM);
            WriteUInt16(bw, numChannels);
            WriteUInt32(bw, (int)sampleRate);
            WriteUInt32(bw, (int)(sampleRate * numChannels * bytesPerSample));
            WriteUInt16(bw, numChannels * bytesPerSample);
            WriteUInt16(bw, 16);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            WriteUInt32(bw, (int)totalDataBytes);
        }

        private static void WriteAdpcmWavHeader(BinaryWriter bw, int numChannels, int bps, uint numSamples, uint sampleRate, int samplesPerBlock)
        {
            int blockSize = AdpcmSampleCountToBlockSize(samplesPerBlock, numChannels, bps);
            uint numBlocks = numSamples / (uint)samplesPerBlock;
            int leftoverSamples = (int)(numSamples % (uint)samplesPerBlock);
            uint totalDataBytes = numBlocks * (uint)blockSize;

            if (leftoverSamples > 0)
                totalDataBytes += (uint)AdpcmAlignBlockSize(AdpcmSampleCountToBlockSize(leftoverSamples, numChannels, bps), numChannels, bps, true);

            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            WriteUInt32(bw, 4 + 8 + 20 + 8 + 4 + 8 + (int)totalDataBytes);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            WriteUInt32(bw, 20);
            WriteUInt16(bw, WAVE_FORMAT_IMA_ADPCM);
            WriteUInt16(bw, numChannels);
            WriteUInt32(bw, (int)sampleRate);
            WriteUInt32(bw, (int)(sampleRate * blockSize / samplesPerBlock));
            WriteUInt16(bw, blockSize);
            WriteUInt16(bw, bps);
            WriteUInt16(bw, 2);
            WriteUInt16(bw, samplesPerBlock);
            bw.Write(Encoding.ASCII.GetBytes("fact"));
            WriteUInt32(bw, 4);
            WriteUInt32(bw, (int)numSamples);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            WriteUInt32(bw, (int)totalDataBytes);
        }
    }
}
