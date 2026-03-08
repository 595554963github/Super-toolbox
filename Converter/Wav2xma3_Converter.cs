using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2xma3_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private const int ENCODED_SAMPLES_IN_ADPCM_BLOCK = 64;
        private const int SAMPLES_IN_ADPCM_BLOCK = 65;
        private const int ADPCM_INTERLEAVE_BYTES = 4;
        private const int ADPCM_INTERLEAVE_SAMPLES = ADPCM_INTERLEAVE_BYTES * 2;
        private const int ADPCM_BLOCK_BYTES = ENCODED_SAMPLES_IN_ADPCM_BLOCK / 2 + 4;

        private const ushort FMT_XBOX_ADPCM = 0x69;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        private struct CImaADPCM
        {
            public short sample;
            public byte index;
            private byte padding;

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

            public short GetSample() => sample;
            public void SetSample(short pcm) => sample = pcm;

            private byte GetNewIndex(byte adpcm)
            {
                byte adpcmLow = (byte)(adpcm & 7);
                byte curIndex = index;
                if ((adpcmLow & 4) != 0)
                {
                    curIndex += (byte)((adpcmLow - 3) * 2);
                    if (curIndex > StepTable.Length - 1)
                        curIndex = (byte)(StepTable.Length - 1);
                }
                else if (curIndex > 0)
                    curIndex--;
                return curIndex;
            }

            public short DecodeSample(byte adpcm)
            {
                int step = StepTable[index];
                index = GetNewIndex(adpcm);

                int diff = step >> 3;
                for (int i = 0; i < 3; i++)
                {
                    if ((adpcm & (4 >> i)) != 0)
                        diff += step >> i;
                }
                if ((adpcm & 8) != 0)
                    diff = -diff;

                sample = (short)Math.Clamp(sample + diff, short.MinValue, short.MaxValue);
                return sample;
            }

            public byte EncodeSample(short pcm)
            {
                int delta = pcm;
                delta -= sample;
                byte adpcm = 0;
                if (delta < 0)
                {
                    delta = -delta;
                    adpcm |= 8;
                }

                int step = StepTable[index];
                int diff = step >> 3;
                int stepVar = step;
                for (int i = 0; i < 3; i++)
                {
                    if (delta > stepVar)
                    {
                        delta -= stepVar;
                        diff += stepVar;
                        adpcm |= (byte)(4 >> i);
                    }
                    stepVar >>= 1;
                }
                if ((adpcm & 8) != 0)
                    diff = -diff;

                sample = (short)Math.Clamp(sample + diff, short.MinValue, short.MaxValue);
                index = GetNewIndex(adpcm);
                return adpcm;
            }
        }

        private long Encode(short[] pcm, byte[] adpcm, int numChannels, int numSamplesPerChannel)
        {
            CImaADPCM[] converters = new CImaADPCM[numChannels];
            int pcmOffset = 0;
            int adpcmOffset = 0;
            int i;

            for (i = 0; i < numSamplesPerChannel; i += SAMPLES_IN_ADPCM_BLOCK)
            {
                for (int c = 0; c < numChannels; c++)
                {
                    converters[c].SetSample(pcm[pcmOffset++]);
                    var span = adpcm.AsSpan(adpcmOffset);
                    System.Runtime.InteropServices.MemoryMarshal.Write(span, in converters[c]);
                    adpcmOffset += 4;
                }

                for (int j = 0; j < ENCODED_SAMPLES_IN_ADPCM_BLOCK / ADPCM_INTERLEAVE_SAMPLES; j++)
                {
                    for (int c = 0; c < numChannels; c++)
                    {
                        for (int k = 0; k < ADPCM_INTERLEAVE_SAMPLES; k++)
                        {
                            byte encodedSample = converters[c].EncodeSample(pcm[pcmOffset + k * numChannels + c]);
                            int byteIndex = adpcmOffset + k / 2;
                            if ((k & 1) != 0)
                                adpcm[byteIndex] |= (byte)(encodedSample << 4);
                            else
                                adpcm[byteIndex] = encodedSample;
                        }
                        adpcmOffset += ADPCM_INTERLEAVE_BYTES;
                    }
                    pcmOffset += ADPCM_INTERLEAVE_SAMPLES * numChannels;
                }
            }
            return i;
        }
        private byte[] CreateXmaHeader(int sampleRate, int dataSize, int channels)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(0x46464952);
                bw.Write((uint)(dataSize + 52));
                bw.Write(0x45564157);
                bw.Write(0x20746D66);
                bw.Write((uint)20);
                bw.Write(FMT_XBOX_ADPCM);
                bw.Write((ushort)channels);
                bw.Write((uint)sampleRate);
                bw.Write((uint)(sampleRate * ADPCM_BLOCK_BYTES * channels >> 6));
                bw.Write((ushort)(ADPCM_BLOCK_BYTES * channels));
                bw.Write((ushort)4);
                bw.Write((ushort)2);
                bw.Write((ushort)ENCODED_SAMPLES_IN_ADPCM_BLOCK);
                bw.Write(0x61746164);
                bw.Write((uint)dataSize);
                return ms.ToArray();
            }
        }
        private short[][] GetChannels(Pcm16Format pcmFormat)
        {
            return pcmFormat.Channels;
        }
        private void Convert(string inFile, string outFile)
        {
            var wavReader = new WaveReader();
            AudioData audioData;

            using (var wavStream = File.OpenRead(inFile))
            {
                audioData = wavReader.Read(wavStream);
            }

            if (audioData == null)
                throw new Exception("读取WAV文件失败");

            var pcmFormat = audioData.GetFormat<Pcm16Format>();
            if (pcmFormat == null)
                throw new Exception("只支持16位PCM");

            short[][] channels = GetChannels(pcmFormat);
            int channelCount = channels.Length;
            int sampleRate = pcmFormat.SampleRate;
            int samplesCount = channels[0].Length;

            int extraSamples = samplesCount % SAMPLES_IN_ADPCM_BLOCK;
            if (extraSamples > 0)
            {
                extraSamples = SAMPLES_IN_ADPCM_BLOCK - extraSamples;
                samplesCount += extraSamples;

                for (int c = 0; c < channelCount; c++)
                {
                    Array.Resize(ref channels[c], samplesCount);
                }
            }

            short[] interleavedPcm = new short[samplesCount * channelCount];
            for (int i = 0; i < samplesCount; i++)
            {
                for (int c = 0; c < channelCount; c++)
                {
                    interleavedPcm[i * channelCount + c] = channels[c][i];
                }
            }

            int blocks = samplesCount / SAMPLES_IN_ADPCM_BLOCK;
            int adpcmDataSize = blocks * ADPCM_BLOCK_BYTES * channelCount;
            byte[] adpcmData = new byte[adpcmDataSize];
            Encode(interleavedPcm, adpcmData, channelCount, samplesCount);

            byte[] header = CreateXmaHeader(sampleRate, adpcmDataSize, channelCount);

            using (var fs = new FileStream(outFile, FileMode.Create))
            {
                fs.Write(header, 0, header.Length);
                fs.Write(adpcmData, 0, adpcmData.Length);
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
                    string xmaFilePath = Path.Combine(fileDirectory, $"{fileName}.xma");

                    try
                    {
                        Convert(wavFilePath, xmaFilePath);

                        if (File.Exists(xmaFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(xmaFilePath)}");
                            OnFileConverted(xmaFilePath);
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
    }
}