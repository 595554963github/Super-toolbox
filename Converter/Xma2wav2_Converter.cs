using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Xma2wav2_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private const int EncodedSamplesInAdpcmBlock = 64;
        private const int SamplesInAdpcmBlock = 65;
        private const int AdpcmInterleaveBytes = 4;
        private const int AdpcmInterleaveSamples = AdpcmInterleaveBytes * 2;
        private const int AdpcmBlockBytes = EncodedSamplesInAdpcmBlock / 2 + 4;

        private static readonly ushort[] StepTable = new ushort[]
        {
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

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ImaAdpcm
        {
            public short Sample;
            public byte Index;
            private byte Padding;

            public ImaAdpcm(short sample, byte index)
            {
                Sample = sample;
                Index = index;
                Padding = 0;
            }

            public short DecodeSample(byte adpcm)
            {
                int step = StepTable[Index];
                Index = GetNewIndex(adpcm);

                int diff = step >> 3;
                for (int i = 0; i < 3; i++)
                {
                    if ((adpcm & (4 >> i)) != 0)
                        diff += step >> i;
                }
                if ((adpcm & 8) != 0) diff = -diff;

                Sample = (short)Math.Clamp(Sample + diff, short.MinValue, short.MaxValue);
                return Sample;
            }

            public byte EncodeSample(short pcm)
            {
                int delta = pcm - Sample;
                byte adpcm = 0;
                if (delta < 0)
                {
                    delta = -delta;
                    adpcm |= 8;
                }

                int step = StepTable[Index];
                int diff = step >> 3;
                int remainingDelta = delta;

                for (int i = 0; i < 3; i++)
                {
                    if (remainingDelta > step)
                    {
                        remainingDelta -= step;
                        diff += step;
                        adpcm |= (byte)(4 >> i);
                    }
                    step >>= 1;
                }
                if ((adpcm & 8) != 0) diff = -diff;

                Sample = (short)Math.Clamp(Sample + diff, short.MinValue, short.MaxValue);
                Index = GetNewIndex(adpcm);
                return adpcm;
            }

            private byte GetNewIndex(byte adpcm)
            {
                adpcm &= 7;
                byte curIndex = Index;
                if ((adpcm & 4) != 0)
                {
                    curIndex += (byte)((adpcm - 3) * 2);
                    if (curIndex >= StepTable.Length)
                        curIndex = (byte)(StepTable.Length - 1);
                }
                else if (curIndex > 0)
                {
                    curIndex--;
                }
                return curIndex;
            }
        }

        private enum WavFormat : ushort
        {
            Pcm = 1,
            XboxAdpcm = 0x69
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RiffChunkHeader
        {
            public uint Id;
            public uint Size;
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

            var xmaFiles = Directory.GetFiles(directoryPath, "*.xma", SearchOption.AllDirectories)
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

            TotalFilesToConvert = xmaFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var xmaFilePath in xmaFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(xmaFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.xma");

                    string fileDirectory = Path.GetDirectoryName(xmaFilePath) ?? string.Empty;

                    try
                    {
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertXmaToWav(xmaFilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.xma转换失败");
                            OnConversionFailed($"{fileName}.xma转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.xma处理错误:{ex.Message}");
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

        private bool ConvertXmaToWav(string xmaFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取XMA文件:{Path.GetFileName(xmaFilePath)}");

                using var fs = File.OpenRead(xmaFilePath);
                using var br = new BinaryReader(fs);

                var riffHeader = ReadStructure<RiffChunkHeader>(br);
                if (riffHeader.Id != 0x46464952)
                {
                    throw new InvalidOperationException("不是有效的RIFF文件");
                }

                uint waveId = br.ReadUInt32();
                if (waveId != 0x45564157)
                {
                    throw new InvalidOperationException("不是有效的WAVE文件");
                }

                ushort numChannels = 0;
                uint samplesPerSec = 0;
                uint dataSize = 0;
                long dataPosition = 0;

                while (fs.Position < fs.Length)
                {
                    if ((fs.Position & 1) != 0)
                        fs.Seek(1, SeekOrigin.Current);

                    if (fs.Position >= fs.Length)
                        break;

                    var chunkHeader = ReadStructure<RiffChunkHeader>(br);

                    if (chunkHeader.Id == 0x20746D66)
                    {
                        long fmtStart = fs.Position;
                        ushort format = br.ReadUInt16();
                        numChannels = br.ReadUInt16();
                        samplesPerSec = br.ReadUInt32();
                        br.ReadUInt32();
                        br.ReadUInt16();
                        br.ReadUInt16();

                        if (format != (ushort)WavFormat.XboxAdpcm)
                        {
                            throw new InvalidOperationException("不是Xbox ADPCM文件");
                        }

                        fs.Seek(fmtStart + chunkHeader.Size, SeekOrigin.Begin);
                    }
                    else if (chunkHeader.Id == 0x61746164)
                    {
                        dataPosition = fs.Position;
                        dataSize = chunkHeader.Size;
                        fs.Seek(chunkHeader.Size, SeekOrigin.Current);
                    }
                    else
                    {
                        fs.Seek(chunkHeader.Size, SeekOrigin.Current);
                    }
                }

                if (numChannels == 0 || samplesPerSec == 0 || dataSize == 0)
                {
                    throw new InvalidOperationException("WAV文件中缺少必需的块");
                }

                int samplesPerChannel = SamplesInAdpcmBlock * (int)(dataSize / (AdpcmBlockBytes * numChannels));

                ConversionProgress?.Invoke(this, $"转换为WAV格式:{Path.GetFileName(wavFilePath)}");

                fs.Seek(dataPosition, SeekOrigin.Begin);
                byte[] adpcmData = br.ReadBytes((int)dataSize);
                short[] pcmData = DecodeAdpcm(adpcmData, numChannels, samplesPerChannel);

                using (var waveStream = new FileStream(wavFilePath, FileMode.Create))
                using (var writer = new BinaryWriter(waveStream))
                {
                    WriteWavHeader(writer, numChannels, samplesPerSec, pcmData.Length * 2);
                    foreach (short sample in pcmData)
                    {
                        writer.Write(sample);
                    }
                }

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

        private void WriteWavHeader(BinaryWriter writer, ushort numChannels, uint samplesPerSec, int dataSize)
        {
            writer.Write(0x46464952);
            writer.Write(36 + dataSize);
            writer.Write(0x45564157);

            writer.Write(0x20746D66);
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write(numChannels);
            writer.Write(samplesPerSec);
            writer.Write(samplesPerSec * numChannels * 2);
            writer.Write((ushort)(numChannels * 2));
            writer.Write((ushort)16);

            writer.Write(0x61746164);
            writer.Write(dataSize);
        }

        private short[] DecodeAdpcm(byte[] adpcmData, ushort numChannels, int samplesPerChannel)
        {
            var converters = new ImaAdpcm[numChannels];
            var pcm = new short[samplesPerChannel * numChannels];
            int adpcmOffset = 0;
            int pcmOffset = 0;

            for (int i = 0; i < samplesPerChannel; i += SamplesInAdpcmBlock)
            {
                for (int c = 0; c < numChannels; c++)
                {
                    converters[c] = ReadStructure<ImaAdpcm>(adpcmData, adpcmOffset);
                    adpcmOffset += 4;
                    pcm[pcmOffset++] = converters[c].Sample;
                }

                for (int j = 0; j < EncodedSamplesInAdpcmBlock / AdpcmInterleaveSamples; j++)
                {
                    for (int c = 0; c < numChannels; c++)
                    {
                        for (int k = 0; k < AdpcmInterleaveSamples; k++)
                        {
                            byte adpcmByte = adpcmData[adpcmOffset + k / 2];
                            byte nibble = (k & 1) != 0 ? (byte)(adpcmByte >> 4) : (byte)(adpcmByte & 0xF);
                            pcm[pcmOffset + k * numChannels + c] = converters[c].DecodeSample(nibble);
                        }
                        adpcmOffset += AdpcmInterleaveBytes;
                    }
                    pcmOffset += AdpcmInterleaveSamples * numChannels;
                }
            }

            return pcm;
        }

        private static T ReadStructure<T>(BinaryReader br) where T : struct
        {
            byte[] buffer = br.ReadBytes(Marshal.SizeOf<T>());
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private static T ReadStructure<T>(byte[] buffer, int offset) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject() + offset;
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                handle.Free();
            }
        }
    }
}