using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2brwav2_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;
        private const byte RWAV_ENCODING_PCM8 = 0;
        private const string RWAV_MAGIC = "RWAV";
        private const ushort RWAV_ENDIANNESS_BIG = 0xFEFF;
        private const ushort RWAV_VERSION = 0x0102;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RwavHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] magic;
            public ushort endianness;
            public ushort version;
            public uint fileSize;
            public ushort headerSize;
            public ushort blockCount;
            public int headBlockOffset;
            public int headBlockSize;
            public int dataBlockOffset;
            public int dataBlockSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RwavBlockHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] magic;
            public uint size;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RwavWaveInfo
        {
            public byte codec;
            public byte looping;
            public byte channelCount;
            public byte padding;
            public ushort sampleRate;
            public ushort padding2;
            public uint loopStart;
            public uint sampleCount;
            public int channelInfoOffset;
            public int infoStructureLength;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RwavChannelInfo
        {
            public int audioDataOffset;
            public int adpcmInfoOffset;
            public int volumeFrontRight;
            public int volumeFrontLeft;
            public int volumeBackRight;
            public int volumeBackLeft;
        }

        private class RWAV
        {
            public uint channels;
            public uint sampleRate;
            public uint sampleCount;
            public uint dataSize;
            public byte[]? data;
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

            var wavFiles = Directory.GetFiles(directoryPath, "*.wav", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = wavFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.wav");

                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;

                    try
                    {
                        string brwavFile = Path.Combine(fileDirectory, $"{fileName}.brwav");

                        if (File.Exists(brwavFile))
                            File.Delete(brwavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToBrwav(wavFilePath, brwavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(brwavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(brwavFile)}");
                            OnFileConverted(brwavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.wav转换失败");
                            OnConversionFailed($"{fileName}.wav转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.wav处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
                }

                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }

        private bool ConvertWavToBrwav(string wavFilePath, string brwavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取wav文件:{Path.GetFileName(wavFilePath)}");

                var waveReader = new WaveReader();
                AudioData audioData;

                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = waveReader.Read(wavStream);
                }

                if (audioData == null)
                {
                    throw new InvalidOperationException("无法读取wav音频数据");
                }

                ConversionProgress?.Invoke(this, $"转换为brwav格式:{Path.GetFileName(brwavFilePath)}");

                var pcm16 = audioData.GetFormat<Pcm16Format>();
                var channelCount = pcm16.ChannelCount;
                var sampleCount = pcm16.SampleCount;
                var audioChannels = pcm16.Channels;

                RWAV rwav = new RWAV();
                rwav.channels = (uint)channelCount;
                rwav.sampleRate = (uint)pcm16.SampleRate;
                rwav.sampleCount = (uint)sampleCount;

                rwav.dataSize = (uint)(sampleCount * rwav.channels * 1);
                rwav.data = new byte[rwav.dataSize];

                for (int ch = 0; ch < channelCount; ch++)
                {
                    short[] channelData = audioChannels[ch];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int dstIdx = (ch * sampleCount + i);
                        short sample = channelData[i];
                        rwav.data[dstIdx] = (byte)((sample >> 8) & 0xFF);
                    }
                }

                byte[] brwavData = BuildBrwav(rwav);
                File.WriteAllBytes(brwavFilePath, brwavData);

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }
        private byte[] BuildBrwav(RWAV rwav)
        {
            int headerSize = 0x20;
            int infoBlockOffset = 0x20;
            int dataBlockOffset = 0xA0;
            int infoBlockSize = 0x80;

            int dataBlockHeaderSize = 8;
            int dataBlockSize = dataBlockHeaderSize + (int)rwav.dataSize;

            int fileSize = headerSize + infoBlockSize + dataBlockSize;

            byte[] output = new byte[fileSize];
            int offset = 0;

            WriteHeader(output, ref offset, rwav, headerSize, infoBlockSize, dataBlockSize, fileSize, infoBlockOffset, dataBlockOffset);

            WriteInfoBlock(output, ref offset, rwav, infoBlockSize);

            WriteDataBlock(output, ref offset, rwav, dataBlockSize);

            return output;
        }
        private void WriteHeader(byte[] output, ref int offset, RWAV rwav, int headerSize, int infoBlockSize, int dataBlockSize, int fileSize, int infoBlockOffset, int dataBlockOffset)
        {
            WriteBytes(output, ref offset, System.Text.Encoding.ASCII.GetBytes(RWAV_MAGIC));
            WriteUInt16BE(output, ref offset, RWAV_ENDIANNESS_BIG);
            WriteUInt16BE(output, ref offset, RWAV_VERSION);
            WriteUInt32BE(output, ref offset, (uint)fileSize);
            WriteUInt16BE(output, ref offset, (ushort)headerSize);
            WriteUInt16BE(output, ref offset, 2);
            WriteInt32BE(output, ref offset, infoBlockOffset);
            WriteInt32BE(output, ref offset, infoBlockSize);
            WriteInt32BE(output, ref offset, dataBlockOffset);
            WriteInt32BE(output, ref offset, dataBlockSize);
        }
        private void WriteInfoBlock(byte[] output, ref int offset, RWAV rwav, int infoBlockSize)
        {
            WriteBytes(output, ref offset, System.Text.Encoding.ASCII.GetBytes("INFO"));
            WriteUInt32BE(output, ref offset, (uint)infoBlockSize);

            WriteByte(output, ref offset, RWAV_ENCODING_PCM8);
            WriteByte(output, ref offset, 0);
            WriteByte(output, ref offset, (byte)rwav.channels);
            WriteByte(output, ref offset, 0);
            WriteUInt16BE(output, ref offset, (ushort)rwav.sampleRate);
            WriteUInt16BE(output, ref offset, 0);
            WriteUInt32BE(output, ref offset, 0);
            WriteUInt32BE(output, ref offset, rwav.sampleCount);

            int channelInfoBase = 0x2C;
            WriteInt32BE(output, ref offset, channelInfoBase);
            WriteInt32BE(output, ref offset, 0);

            WriteInt32BE(output, ref offset, (int)rwav.channels);

            for (int c = 0; c < rwav.channels; c++)
            {
                WriteInt32BE(output, ref offset, channelInfoBase + c * 0x18);
            }

            for (int c = 0; c < rwav.channels; c++)
            {
                WriteInt32BE(output, ref offset, 0x20 + c * (int)(rwav.dataSize / rwav.channels));
                WriteInt32BE(output, ref offset, -1);
                WriteInt32BE(output, ref offset, 0x10000);
                WriteInt32BE(output, ref offset, 0x10000);
                WriteInt32BE(output, ref offset, 0);
                WriteInt32BE(output, ref offset, 0);
            }

            while (offset - 0x20 < infoBlockSize)
            {
                WriteByte(output, ref offset, 0);
            }
        }

        private void WriteDataBlock(byte[] output, ref int offset, RWAV rwav, int dataBlockSize)
        {
            WriteBytes(output, ref offset, System.Text.Encoding.ASCII.GetBytes("DATA"));
            WriteUInt32BE(output, ref offset, (uint)dataBlockSize);

            if (rwav.data != null)
            {
                Array.Copy(rwav.data, 0, output, offset, rwav.data.Length);
                offset += (int)rwav.dataSize;
            }
        }

        private void WriteBytes(byte[] output, ref int offset, byte[] data)
        {
            Array.Copy(data, 0, output, offset, data.Length);
            offset += data.Length;
        }

        private void WriteByte(byte[] output, ref int offset, byte value)
        {
            output[offset++] = value;
        }

        private void WriteUInt16BE(byte[] output, ref int offset, ushort value)
        {
            output[offset++] = (byte)((value >> 8) & 0xFF);
            output[offset++] = (byte)(value & 0xFF);
        }

        private void WriteUInt32BE(byte[] output, ref int offset, uint value)
        {
            output[offset++] = (byte)((value >> 24) & 0xFF);
            output[offset++] = (byte)((value >> 16) & 0xFF);
            output[offset++] = (byte)((value >> 8) & 0xFF);
            output[offset++] = (byte)(value & 0xFF);
        }

        private void WriteInt32BE(byte[] output, ref int offset, int value)
        {
            WriteUInt32BE(output, ref offset, (uint)value);
        }
    }
}