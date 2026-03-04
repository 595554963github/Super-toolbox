using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2bcwav1_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private const uint CWAV_ENCODING_PCM16 = 1;

        private const uint CWAV_REF_SAMPLE_DATA = 0x1F00;
        private const uint CWAV_REF_INFO_BLOCK = 0x7000;
        private const uint CWAV_REF_DATA_BLOCK = 0x7001;
        private const uint CWAV_REF_CHANNEL_INFO = 0x7100;

        private const string CWAV_MAGIC = "CWAV";
        private const ushort CWAV_ENDIANNESS_LITTLE = 0xFEFF;
        private const uint CWAV_VERSION = 0x02010000;
        private const string CWAV_BLOCK_MAGIC_INFO = "INFO";
        private const string CWAV_BLOCK_MAGIC_DATA = "DATA";

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CWAVReference
        {
            public ushort typeId;
            public ushort padding;
            public uint offset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CWAVSizedReference
        {
            public CWAVReference ref_;
            public uint size;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CWAVReferenceTable
        {
            public uint count;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CWAVHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] magic;
            public ushort endianness;
            public ushort headerSize;
            public uint version;
            public uint fileSize;
            public ushort numBlocks;
            public ushort reserved;
            public CWAVSizedReference infoBlock;
            public CWAVSizedReference dataBlock;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CWAVBlockHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] magic;
            public uint size;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CWAVInfoBlockHeader
        {
            public CWAVBlockHeader header;
            public byte encoding;
            public byte loop;
            public ushort padding;
            public uint sampleRate;
            public uint loopStartFrame;
            public uint loopEndFrame;
            public uint reserved;
            public uint channelCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public CWAVReference[] channelRefs;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CWAVChannelInfo
        {
            public CWAVReference samples;
            public CWAVReference adpcmInfo;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CWAVDataBlock
        {
            public CWAVBlockHeader header;
        }

        private class CWAV
        {
            public uint channels;
            public uint sampleRate;
            public uint loopEndFrame;
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
                        string bcwavFile = Path.Combine(fileDirectory, $"{fileName}.bcwav");

                        if (File.Exists(bcwavFile))
                            File.Delete(bcwavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToBcwav(wavFilePath, bcwavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(bcwavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(bcwavFile)}");
                            OnFileConverted(bcwavFile);
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

        private bool ConvertWavToBcwav(string wavFilePath, string bcwavFilePath, CancellationToken cancellationToken)
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

                ConversionProgress?.Invoke(this, $"转换为bcwav格式:{Path.GetFileName(bcwavFilePath)}");

                var pcm16 = audioData.GetFormat<Pcm16Format>();
                var channelCount = pcm16.ChannelCount;
                var sampleCount = pcm16.SampleCount;
                var audioChannels = pcm16.Channels;

                CWAV cwav = new CWAV();
                cwav.channels = (uint)channelCount;
                cwav.sampleRate = (uint)pcm16.SampleRate;
                cwav.loopEndFrame = (uint)sampleCount;

                cwav.dataSize = (uint)(sampleCount * cwav.channels * 2);
                cwav.data = new byte[cwav.dataSize];

                for (int ch = 0; ch < channelCount; ch++)
                {
                    short[] channelData = audioChannels[ch];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int dstIdx = (ch * sampleCount + i) * 2;
                        short sample = channelData[i];
                        cwav.data[dstIdx] = (byte)(sample & 0xFF);
                        cwav.data[dstIdx + 1] = (byte)((sample >> 8) & 0xFF);
                    }
                }

                byte[] bcwavData = BuildBcwav(cwav);
                File.WriteAllBytes(bcwavFilePath, bcwavData);

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

        private byte[] BuildBcwav(CWAV cwav)
        {
            uint headerSize = (uint)((Marshal.SizeOf<CWAVHeader>() + 0x1F) & ~0x1F);

            uint infoBaseSize = (uint)Marshal.SizeOf<CWAVInfoBlockHeader>();
            uint refTableSize = (uint)(Marshal.SizeOf<CWAVReference>() * cwav.channels);
            uint channelInfoSize = (uint)(Marshal.SizeOf<CWAVChannelInfo>() * cwav.channels);

            uint infoSize = infoBaseSize + refTableSize + channelInfoSize;
            infoSize = (uint)((infoSize + 0x1F) & ~0x1F);

            uint dataBlockHeaderSize = (uint)Marshal.SizeOf<CWAVDataBlock>();
            uint dataBlockAlignedSize = (uint)((dataBlockHeaderSize + 0x1F) & ~0x1F);
            uint dataStartOffset = dataBlockAlignedSize - dataBlockHeaderSize;

            uint dataBlockTotalSize = dataBlockAlignedSize + cwav.dataSize;

            uint outputSize = headerSize + infoSize + dataBlockTotalSize;

            byte[] output = new byte[outputSize];
            int offset = 0;

            CWAVHeader header = new CWAVHeader();
            header.magic = System.Text.Encoding.ASCII.GetBytes(CWAV_MAGIC);
            header.endianness = CWAV_ENDIANNESS_LITTLE;
            header.headerSize = (ushort)headerSize;
            header.version = CWAV_VERSION;
            header.fileSize = outputSize;
            header.numBlocks = 2;
            header.reserved = 0;

            header.infoBlock = new CWAVSizedReference();
            header.infoBlock.ref_ = new CWAVReference();
            header.infoBlock.ref_.typeId = (ushort)CWAV_REF_INFO_BLOCK;
            header.infoBlock.ref_.padding = 0;
            header.infoBlock.ref_.offset = headerSize;
            header.infoBlock.size = infoSize;

            header.dataBlock = new CWAVSizedReference();
            header.dataBlock.ref_ = new CWAVReference();
            header.dataBlock.ref_.typeId = (ushort)CWAV_REF_DATA_BLOCK;
            header.dataBlock.ref_.padding = 0;
            header.dataBlock.ref_.offset = headerSize + infoSize;
            header.dataBlock.size = dataBlockTotalSize;

            offset += WriteStructure(output, offset, header);
            offset = (int)headerSize;

            CWAVBlockHeader infoBlockHeader = new CWAVBlockHeader();
            infoBlockHeader.magic = System.Text.Encoding.ASCII.GetBytes(CWAV_BLOCK_MAGIC_INFO);
            infoBlockHeader.size = infoSize;
            offset += WriteStructure(output, offset, infoBlockHeader);

            offset += WriteValue(output, offset, (byte)CWAV_ENCODING_PCM16);
            offset += WriteValue(output, offset, (byte)0);
            offset += WriteValue(output, offset, (ushort)0);
            offset += WriteValue(output, offset, cwav.sampleRate);
            offset += WriteValue(output, offset, 0u);
            offset += WriteValue(output, offset, cwav.loopEndFrame);
            offset += WriteValue(output, offset, 0u);
            offset += WriteValue(output, offset, cwav.channels);

            uint channelRefBaseOffset = (uint)(Marshal.SizeOf<CWAVReferenceTable>() +
                                              (cwav.channels * Marshal.SizeOf<CWAVReference>()));

            for (uint c = 0; c < cwav.channels; c++)
            {
                CWAVReference channelRef = new CWAVReference();
                channelRef.typeId = (ushort)CWAV_REF_CHANNEL_INFO;
                channelRef.padding = 0;
                channelRef.offset = channelRefBaseOffset + (c * (uint)Marshal.SizeOf<CWAVChannelInfo>());
                offset += WriteStructure(output, offset, channelRef);
            }

            for (uint c = 0; c < cwav.channels; c++)
            {
                CWAVChannelInfo channelInfo = new CWAVChannelInfo();

                channelInfo.samples = new CWAVReference();
                channelInfo.samples.typeId = (ushort)CWAV_REF_SAMPLE_DATA;
                channelInfo.samples.padding = 0;
                channelInfo.samples.offset = dataStartOffset + (c * (cwav.dataSize / cwav.channels));

                channelInfo.adpcmInfo = new CWAVReference();
                channelInfo.adpcmInfo.typeId = 0;
                channelInfo.adpcmInfo.padding = 0;
                channelInfo.adpcmInfo.offset = 0xFFFFFFFF;

                channelInfo.reserved = 0;

                offset += WriteStructure(output, offset, channelInfo);
            }

            while ((offset % 0x20) != 0)
            {
                offset += WriteValue(output, offset, (byte)0);
            }

            offset = (int)(headerSize + infoSize);

            CWAVBlockHeader dataBlockHeader = new CWAVBlockHeader();
            dataBlockHeader.magic = System.Text.Encoding.ASCII.GetBytes(CWAV_BLOCK_MAGIC_DATA);
            dataBlockHeader.size = dataBlockTotalSize;
            offset += WriteStructure(output, offset, dataBlockHeader);

            offset = (int)(headerSize + infoSize + dataBlockAlignedSize);

            if (cwav.data != null)
            {
                Array.Copy(cwav.data, 0, output, offset, (int)cwav.dataSize);
            }

            return output;
        }

        private int WriteStructure<T>(byte[] buffer, int offset, T structure) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(structure, ptr, false);
                Marshal.Copy(ptr, buffer, offset, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return size;
        }

        private int WriteValue<T>(byte[] buffer, int offset, T value) where T : unmanaged
        {
            int size = Marshal.SizeOf<T>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, ptr, false);
                Marshal.Copy(ptr, buffer, offset, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return size;
        }
    }
}
