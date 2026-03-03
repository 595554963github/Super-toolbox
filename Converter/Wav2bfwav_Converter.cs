using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2bfwav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private const uint FWAV_ENCODING_PCM16 = 1;

        private const uint FWAV_REF_SAMPLE_DATA = 0x1F00;
        private const uint FWAV_REF_INFO_BLOCK = 0x7000;
        private const uint FWAV_REF_DATA_BLOCK = 0x7001;
        private const uint FWAV_REF_CHANNEL_INFO = 0x7100;

        private const string FWAV_MAGIC = "FWAV";
        private const ushort FWAV_ENDIANNESS_BIG = 0xFEFF;
        private const uint FWAV_VERSION = 0x00010100;
        private const string FWAV_BLOCK_MAGIC_INFO = "INFO";
        private const string FWAV_BLOCK_MAGIC_DATA = "DATA";

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FWAVReference
        {
            public ushort typeId;
            public ushort padding;
            public uint offset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FWAVSizedReference
        {
            public FWAVReference ref_;
            public uint size;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FWAVReferenceTable
        {
            public uint count;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FWAVHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] magic;
            public ushort endianness;
            public ushort headerSize;
            public uint version;
            public uint fileSize;
            public ushort numBlocks;
            public ushort reserved;
            public FWAVSizedReference infoBlock;
            public FWAVSizedReference dataBlock;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FWAVBlockHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] magic;
            public uint size;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FWAVInfoBlockHeader
        {
            public FWAVBlockHeader header;
            public byte encoding;
            public byte loop;
            public ushort padding;
            public uint sampleRate;
            public uint loopStartFrame;
            public uint loopEndFrame;
            public uint reserved;
            public uint channelCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public FWAVReference[] channelRefs;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FWAVChannelInfo
        {
            public FWAVReference samples;
            public FWAVReference adpcmInfo;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FWAVDataBlock
        {
            public FWAVBlockHeader header;
        }

        private class FWAV
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
                        string bfwavFile = Path.Combine(fileDirectory, $"{fileName}.bfwav");

                        if (File.Exists(bfwavFile))
                            File.Delete(bfwavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToBfwav(wavFilePath, bfwavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(bfwavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(bfwavFile)}");
                            OnFileConverted(bfwavFile);
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

        private bool ConvertWavToBfwav(string wavFilePath, string bfwavFilePath, CancellationToken cancellationToken)
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

                ConversionProgress?.Invoke(this, $"转换为bfwav格式:{Path.GetFileName(bfwavFilePath)}");

                var pcm16 = audioData.GetFormat<Pcm16Format>();
                var channelCount = pcm16.ChannelCount;
                var sampleCount = pcm16.SampleCount;
                var audioChannels = pcm16.Channels;

                FWAV fwav = new FWAV();
                fwav.channels = (uint)channelCount;
                fwav.sampleRate = (uint)pcm16.SampleRate;
                fwav.loopEndFrame = (uint)sampleCount;

                fwav.dataSize = (uint)(sampleCount * fwav.channels * 2);
                fwav.data = new byte[fwav.dataSize];

                for (int ch = 0; ch < channelCount; ch++)
                {
                    short[] channelData = audioChannels[ch];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int dstIdx = (ch * sampleCount + i) * 2;
                        short sample = channelData[i];
                        fwav.data[dstIdx] = (byte)(sample & 0xFF);
                        fwav.data[dstIdx + 1] = (byte)((sample >> 8) & 0xFF);
                    }
                }

                byte[] bfwavData = BuildBfwav(fwav);
                File.WriteAllBytes(bfwavFilePath, bfwavData);

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

        private byte[] BuildBfwav(FWAV fwav)
        {
            uint headerSize = (uint)((Marshal.SizeOf<FWAVHeader>() + 0x1F) & ~0x1F);

            uint infoBaseSize = (uint)Marshal.SizeOf<FWAVInfoBlockHeader>();
            uint refTableSize = (uint)(Marshal.SizeOf<FWAVReference>() * fwav.channels);
            uint channelInfoSize = (uint)(Marshal.SizeOf<FWAVChannelInfo>() * fwav.channels);

            uint infoSize = infoBaseSize + refTableSize + channelInfoSize;
            infoSize = (uint)((infoSize + 0x1F) & ~0x1F);

            uint dataBlockHeaderSize = (uint)Marshal.SizeOf<FWAVDataBlock>();
            uint dataBlockAlignedSize = (uint)((dataBlockHeaderSize + 0x1F) & ~0x1F);
            uint dataStartOffset = dataBlockAlignedSize - dataBlockHeaderSize;

            uint dataBlockTotalSize = dataBlockAlignedSize + fwav.dataSize;

            uint outputSize = headerSize + infoSize + dataBlockTotalSize;

            byte[] output = new byte[outputSize];
            int offset = 0;

            FWAVHeader header = new FWAVHeader();
            header.magic = System.Text.Encoding.ASCII.GetBytes(FWAV_MAGIC);
            header.endianness = FWAV_ENDIANNESS_BIG;
            header.headerSize = (ushort)headerSize;
            header.version = FWAV_VERSION;
            header.fileSize = outputSize;
            header.numBlocks = 2;
            header.reserved = 0;

            header.infoBlock = new FWAVSizedReference();
            header.infoBlock.ref_ = new FWAVReference();
            header.infoBlock.ref_.typeId = (ushort)FWAV_REF_INFO_BLOCK;
            header.infoBlock.ref_.padding = 0;
            header.infoBlock.ref_.offset = headerSize;
            header.infoBlock.size = infoSize;

            header.dataBlock = new FWAVSizedReference();
            header.dataBlock.ref_ = new FWAVReference();
            header.dataBlock.ref_.typeId = (ushort)FWAV_REF_DATA_BLOCK;
            header.dataBlock.ref_.padding = 0;
            header.dataBlock.ref_.offset = headerSize + infoSize;
            header.dataBlock.size = dataBlockTotalSize;

            offset += WriteStructure(output, offset, header);
            offset = (int)headerSize;

            FWAVBlockHeader infoBlockHeader = new FWAVBlockHeader();
            infoBlockHeader.magic = System.Text.Encoding.ASCII.GetBytes(FWAV_BLOCK_MAGIC_INFO);
            infoBlockHeader.size = infoSize;
            offset += WriteStructure(output, offset, infoBlockHeader);

            offset += WriteValue(output, offset, (byte)FWAV_ENCODING_PCM16);
            offset += WriteValue(output, offset, (byte)0);
            offset += WriteValue(output, offset, (ushort)0);
            offset += WriteValue(output, offset, fwav.sampleRate);
            offset += WriteValue(output, offset, 0u);
            offset += WriteValue(output, offset, fwav.loopEndFrame);
            offset += WriteValue(output, offset, 0u);
            offset += WriteValue(output, offset, fwav.channels);

            uint channelRefBaseOffset = (uint)(Marshal.SizeOf<FWAVReferenceTable>() +
                                              (fwav.channels * Marshal.SizeOf<FWAVReference>()));

            for (uint c = 0; c < fwav.channels; c++)
            {
                FWAVReference channelRef = new FWAVReference();
                channelRef.typeId = (ushort)FWAV_REF_CHANNEL_INFO;
                channelRef.padding = 0;
                channelRef.offset = channelRefBaseOffset + (c * (uint)Marshal.SizeOf<FWAVChannelInfo>());
                offset += WriteStructure(output, offset, channelRef);
            }

            for (uint c = 0; c < fwav.channels; c++)
            {
                FWAVChannelInfo channelInfo = new FWAVChannelInfo();

                channelInfo.samples = new FWAVReference();
                channelInfo.samples.typeId = (ushort)FWAV_REF_SAMPLE_DATA;
                channelInfo.samples.padding = 0;
                channelInfo.samples.offset = dataStartOffset + (c * (fwav.dataSize / fwav.channels));

                channelInfo.adpcmInfo = new FWAVReference();
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

            FWAVBlockHeader dataBlockHeader = new FWAVBlockHeader();
            dataBlockHeader.magic = System.Text.Encoding.ASCII.GetBytes(FWAV_BLOCK_MAGIC_DATA);
            dataBlockHeader.size = dataBlockTotalSize;
            offset += WriteStructure(output, offset, dataBlockHeader);

            offset = (int)(headerSize + infoSize + dataBlockAlignedSize);

            if (fwav.data != null)
            {
                Array.Copy(fwav.data, 0, output, offset, (int)fwav.dataSize);
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