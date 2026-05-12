namespace super_toolbox
{
    internal class AsfEncoder
    {
        private readonly int _revision;
        private readonly int _nChannels;
        private readonly int _sampleRate;
        private readonly int _nSamples;
        private readonly EaXaEncoder[] _encoders;
        private int _offsetSCClValue;

        public AsfEncoder(int revision, int nChannels, int sampleRate, int nSamples)
        {
            _revision = revision;
            _nChannels = nChannels;
            _sampleRate = sampleRate;
            _nSamples = nSamples;
            _encoders = new EaXaEncoder[nChannels];
            for (int c = 0; c < nChannels; c++)
                _encoders[c] = new EaXaEncoder();
        }

        public void WriteFile(Stream outStream, short[][] channels)
        {
            byte[] header = WriteHeader(out int headerSize);
            outStream.Write(header, 0, headerSize);

            int codedSamples = 0;
            int blockIndex = 1;
            bool lastBlock = false;
            int blocksPerSecond = _revision == 3 ? 5 : 15;
            int writtenBlocks = 0;

            while (!lastBlock)
            {
                int samplesInBlock = (int)(((long)blockIndex * _sampleRate) / blocksPerSecond) - codedSamples;
                int modulo28 = samplesInBlock % 28;
                if (modulo28 != 0)
                    samplesInBlock = samplesInBlock + 28 - modulo28;
                codedSamples += samplesInBlock;
                if (codedSamples >= _nSamples)
                {
                    int toRemove = codedSamples - _nSamples;
                    samplesInBlock -= toRemove;
                    codedSamples = _nSamples;
                    lastBlock = true;
                }

                short[] blockSamples = new short[samplesInBlock * _nChannels];
                for (int c = 0; c < _nChannels; c++)
                {
                    var ch = channels[c];
                    int srcStart = codedSamples - samplesInBlock;
                    int srcCount = Math.Min(samplesInBlock, Math.Max(0, ch.Length - srcStart));
                    if (srcCount > 0)
                        Array.Copy(ch, srcStart, blockSamples, c * samplesInBlock, srcCount);
                    for (int i = srcCount; i < samplesInBlock; i++)
                        blockSamples[c * samplesInBlock + i] = 0;
                }

                if (blockIndex == 1 && _revision == 1)
                {
                    for (int c = 0; c < _nChannels; c++)
                    {
                        _encoders[c].CurrentSample = blockSamples[c * samplesInBlock];
                        _encoders[c].PreviousSample = blockSamples[c * samplesInBlock];
                    }
                }

                byte[] blockData = WriteSCDlBlock(samplesInBlock, blockSamples, blockIndex == 1);
                outStream.Write(blockData, 0, blockData.Length);
                writtenBlocks++;
                blockIndex++;
            }

            WriteNumberOfBlocks(outStream, (uint)writtenBlocks);

            byte[] sce = new byte[] { (byte)'S', (byte)'C', (byte)'E', (byte)'l', 0x08, 0x00, 0x00, 0x00 };
            outStream.Write(sce, 0, 8);
        }

        private byte[] WriteHeader(out int rSize)
        {
            byte[] data = new byte[128];
            int pos = 0;

            // SCHl block header
            data[pos++] = (byte)'S'; data[pos++] = (byte)'C'; data[pos++] = (byte)'H'; data[pos++] = (byte)'l';
            pos += 4; // size placeholder at bytes 4-7

            if (_revision == 3)
            {
                data[pos++] = (byte)'G'; data[pos++] = (byte)'S'; data[pos++] = (byte)'T'; data[pos++] = (byte)'R';
                data[pos++] = 0x01; data[pos++] = 0x00; data[pos++] = 0x00; data[pos++] = 0x00;
            }
            else
            {
                data[pos++] = (byte)'P'; data[pos++] = (byte)'T'; data[pos++] = 0x00; data[pos++] = 0x00;
            }

            data[pos++] = 0x06; data[pos++] = 0x01; data[pos++] = 0x65;

            if (_nChannels != 1 && _revision == 1)
            {
                data[pos++] = 0x0B;
                data[pos++] = 0x01;
                data[pos++] = (byte)_nChannels;
            }

            data[pos++] = 0xFD; // Audio subheader

            data[pos++] = 0x80;
            data[pos++] = 0x01;
            data[pos++] = (byte)_revision;

            data[pos++] = 0x85;
            pos += WriteHeaderTag(data, pos, _nSamples);

            if (_nChannels != 1)
            {
                data[pos++] = 0x82;
                data[pos++] = 0x01;
                data[pos++] = (byte)_nChannels;
            }

            if ((_revision < 3 && _sampleRate != 22050) || (_revision == 3 && _sampleRate != 48000))
            {
                data[pos++] = 0x84;
                pos += WriteHeaderTag(data, pos, _sampleRate);
            }

            data[pos++] = 0xFF; // End of SCHl header

            // Padding to 4-byte alignment
            int padding = 4 - (pos % 4);
            if (padding < 4)
            {
                for (int i = 0; i < padding; i++)
                    data[pos++] = 0;
            }

            // SCHl size (at bytes 4-7)
            int schlSize = pos;
            WriteLE32(data, 4, (uint)schlSize);

            data[pos++] = (byte)'S'; data[pos++] = (byte)'C'; data[pos++] = (byte)'C'; data[pos++] = (byte)'l';
            WriteLE32(data, pos, 0x0C); pos += 4;
            _offsetSCClValue = pos; // This is schlSize + 8
            pos += 4; // placeholder for block count

            rSize = pos;

            byte[] result = new byte[pos];
            Array.Copy(data, result, pos);
            return result;
        }

        private byte[] WriteSCDlBlock(int nbSamples, short[] samples, bool firstBlock)
        {
            int nCompleteSubblocks = nbSamples / 28;
            int nSamplesExtraSubblock = nbSamples % 28;

            int maxSize = 16 + _nChannels * (184 + 15 + nCompleteSubblocks * 15 + 61);
            byte[] output = new byte[maxSize];
            int pos = 0;

            // SCDl header
            output[pos++] = (byte)'S'; output[pos++] = (byte)'C'; output[pos++] = (byte)'D'; output[pos++] = (byte)'l';
            pos += 4; // size placeholder

            // nbSamples
            if (_revision == 3)
                WriteBE32(output, pos, (uint)nbSamples);
            else
                WriteLE32(output, pos, (uint)nbSamples);
            pos += 4;

            // Per-channel offsets
            int offsetsPos = pos;
            pos += 4 * _nChannels;
            int channelBlocksStart = pos;

            for (int c = 0; c < _nChannels; c++)
            {
                int channelOffset = pos - channelBlocksStart;
                if (_revision == 3)
                    WriteBE32(output, offsetsPos + c * 4, (uint)channelOffset);
                else
                    WriteLE32(output, offsetsPos + c * 4, (uint)channelOffset);

                int channelSampleOffset = c * nbSamples;
                EaXaEncoder encoder = _encoders[c];

                if (_revision == 1)
                {
                    // Write prediction start samples, then clear errors
                    WriteLE16(output, pos, (ushort)encoder.CurrentSample); pos += 2;
                    WriteLE16(output, pos, (ushort)encoder.PreviousSample); pos += 2;
                    encoder.ClearErrors();
                }

                int i = 0;
                if (firstBlock && _revision > 1)
                {
                    for (; i < 3 && i < nCompleteSubblocks; i++)
                    {
                        var type = (i == 2 && nCompleteSubblocks != 3) ? UncompressedType.FadeToCompressed : UncompressedType.Normal;
                        encoder.WriteUncompressedSubblock(samples, channelSampleOffset, output, pos, 28, type);
                        channelSampleOffset += 28;
                        pos += 61;
                    }
                }

                for (; i < nCompleteSubblocks; i++)
                {
                    encoder.EncodeSubblock(samples, channelSampleOffset, output, pos, 28);
                    channelSampleOffset += 28;
                    pos += 15;
                }

                if (nSamplesExtraSubblock != 0)
                {
                    if (_revision == 1)
                    {
                        for (int z = 0; z < 15; z++) output[pos + z] = 0;
                        encoder.EncodeSubblock(samples, channelSampleOffset, output, pos, nSamplesExtraSubblock);
                        pos += 15;
                    }
                    else
                    {
                        var type = nCompleteSubblocks <= 3 ? UncompressedType.Normal : UncompressedType.FadeFromCompressed;
                        encoder.WriteUncompressedSubblock(samples, channelSampleOffset, output, pos, nSamplesExtraSubblock, type);
                        pos += 61;
                    }
                }

                // Byte padding
                if ((pos & 1) != 0)
                    output[pos++] = 0;
            }

            // 16-bit padding
            uint scdlSize = (uint)pos;
            if ((scdlSize & 2) != 0)
            {
                output[pos++] = 0;
                output[pos++] = 0;
                scdlSize += 2;
            }

            WriteLE32(output, 4, scdlSize);

            byte[] result = new byte[pos];
            Array.Copy(output, result, pos);
            return result;
        }

        private void WriteNumberOfBlocks(Stream outFile, uint nBlocks)
        {
            long prevPos = outFile.Position;
            outFile.Seek(_offsetSCClValue, SeekOrigin.Begin);
            byte[] buf = new byte[4];
            if (_revision == 3)
                WriteBE32(buf, 0, nBlocks);
            else
                WriteLE32(buf, 0, nBlocks);
            outFile.Write(buf, 0, 4);
            outFile.Seek(prevPos, SeekOrigin.Begin);
        }

        private static int WriteHeaderTag(byte[] data, int pos, int value)
        {
            int sizeNeeded;
            if (value > 127 || value < -128)
            {
                if (value > 32767 || value < -32768)
                {
                    if (value > 8388352 || value < -8388608)
                        sizeNeeded = 4;
                    else
                        sizeNeeded = 3;
                }
                else
                    sizeNeeded = 2;
            }
            else
                sizeNeeded = 1;

            data[pos] = (byte)sizeNeeded;

            byte[] beBytes = new byte[4];
            WriteBE32(beBytes, 0, (uint)value);
            for (int i = 0; i < sizeNeeded; i++)
                data[pos + 1 + i] = beBytes[4 - sizeNeeded + i];

            return sizeNeeded + 1;
        }

        private static void WriteLE32(byte[] buf, int pos, uint val)
        {
            buf[pos] = (byte)(val & 0xFF);
            buf[pos + 1] = (byte)((val >> 8) & 0xFF);
            buf[pos + 2] = (byte)((val >> 16) & 0xFF);
            buf[pos + 3] = (byte)((val >> 24) & 0xFF);
        }

        private static void WriteBE32(byte[] buf, int pos, uint val)
        {
            buf[pos] = (byte)((val >> 24) & 0xFF);
            buf[pos + 1] = (byte)((val >> 16) & 0xFF);
            buf[pos + 2] = (byte)((val >> 8) & 0xFF);
            buf[pos + 3] = (byte)(val & 0xFF);
        }

        private static void WriteLE16(byte[] buf, int pos, ushort val)
        {
            buf[pos] = (byte)(val & 0xFF);
            buf[pos + 1] = (byte)((val >> 8) & 0xFF);
        }
    }
}