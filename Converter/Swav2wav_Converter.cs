using System.Text;
using VGAudio.Containers.Wave;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Swav2wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private const int WAVE_PCM8 = 0;
        private const int WAVE_PCM16 = 1;
        private const int WAVE_ADPCM = 2;

        private static readonly int[] ADPCMTable = new int[89]
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

        private static readonly int[] IMAIndexTable = new int[16]
        {
            -1, -1, -1, -1, 2, 4, 6, 8,
            -1, -1, -1, -1, 2, 4, 6, 8
        };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var swavFiles = Directory.GetFiles(directoryPath, "*.swav", SearchOption.AllDirectories)
                        .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                        .ToArray();

                    TotalFilesToConvert = swavFiles.Length;
                    int successCount = 0;

                    if (swavFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的SWAV文件");
                        OnConversionFailed("未找到需要转换的SWAV文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个SWAV文件");

                    foreach (var swavFilePath in swavFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(swavFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.swav");

                        string fileDirectory = Path.GetDirectoryName(swavFilePath) ?? string.Empty;
                        string outputWavPath = Path.Combine(fileDirectory, $"{fileName}.wav");

                        try
                        {
                            ConvertSingleSwavToWav(swavFilePath, outputWavPath);

                            successCount++;
                            ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav");
                            OnFileConverted(outputWavPath);
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.swav处理错误:{ex.Message}");
                        }
                    }

                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
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
                ConversionError?.Invoke(this, $"转换失败:{ex.Message}");
                OnConversionFailed($"转换失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private void ConvertSingleSwavToWav(string swavFilePath, string outputWavPath)
        {
            using (var fs = new FileStream(swavFilePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                string swavMagic = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (swavMagic != "SWAV")
                    throw new InvalidDataException("无效的SWAV文件格式");

                uint magic = br.ReadUInt32();
                uint fileSize = br.ReadUInt32();
                ushort headerSize = br.ReadUInt16();
                ushort blockCount = br.ReadUInt16();

                string dataMagic = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (dataMagic != "DATA")
                    throw new InvalidDataException("无效的DATA块格式");

                uint dataChunkSize = br.ReadUInt32();

                byte waveType = br.ReadByte();
                byte loopFlag = br.ReadByte();
                ushort sampleRate = br.ReadUInt16();
                ushort time = br.ReadUInt16();
                ushort loopOffset = br.ReadUInt16();
                uint nonLoopLen = br.ReadUInt32();

                byte[] audioData = br.ReadBytes((int)(dataChunkSize - 0x14));

                short[] pcmData = DecodeAudioData(audioData, waveType, nonLoopLen);

                short[][] stereoChannels = new short[2][];
                stereoChannels[0] = pcmData;
                stereoChannels[1] = new short[pcmData.Length];
                Array.Copy(pcmData, stereoChannels[1], pcmData.Length);

                var stereoFormat = new Pcm16Format(stereoChannels, sampleRate);
                var wavWriter = new WaveWriter();

                using (var outFs = File.Create(outputWavPath))
                {
                    wavWriter.WriteToStream(stereoFormat, outFs);
                }
            }
        }

        private short[] DecodeAudioData(byte[] data, int waveType, uint sampleCount)
        {
            switch (waveType)
            {
                case WAVE_PCM8:
                    return DecodePcm8(data);
                case WAVE_PCM16:
                    return DecodePcm16(data);
                case WAVE_ADPCM:
                    return DecodeImaAdpcm(data, sampleCount);
                default:
                    throw new NotSupportedException($"不支持的音频格式:{waveType}");
            }
        }

        private short[] DecodePcm8(byte[] data)
        {
            short[] result = new short[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (short)((sbyte)data[i] << 8);
            }
            return result;
        }

        private short[] DecodePcm16(byte[] data)
        {
            if (data.Length % 2 != 0)
                throw new InvalidDataException("PCM16数据长度必须是2的倍数");

            short[] result = new short[data.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = BitConverter.ToInt16(data, i * 2);
            }
            return result;
        }

        private short[] DecodeImaAdpcm(byte[] data, uint expectedSampleCount)
        {
            if (data.Length < 4)
                throw new InvalidDataException("ADPCM数据长度不足");

            int predictor = BitConverter.ToInt16(data, 0);
            int stepIndex = data[2];

            stepIndex = Math.Clamp(stepIndex, 0, 88);

            var pcmSamples = new System.Collections.Generic.List<short>();

            int byteIndex = 4;
            bool useHighNibble = false;

            for (int i = 0; i < expectedSampleCount; i++)
            {
                pcmSamples.Add((short)predictor);

                if (byteIndex >= data.Length) break;

                byte currentByte = data[byteIndex];
                int nibble;

                if (!useHighNibble)
                {
                    nibble = currentByte & 0x0F;
                    useHighNibble = true;
                }
                else
                {
                    nibble = (currentByte >> 4) & 0x0F;
                    useHighNibble = false;
                    byteIndex++;
                }

                int step = ADPCMTable[stepIndex];
                int delta = step >> 3;

                if ((nibble & 1) != 0) delta += step >> 2;
                if ((nibble & 2) != 0) delta += step >> 1;
                if ((nibble & 4) != 0) delta += step;

                if ((nibble & 8) != 0)
                {
                    predictor -= delta;
                }
                else
                {
                    predictor += delta;
                }

                predictor = Math.Clamp(predictor, short.MinValue, short.MaxValue);

                stepIndex += IMAIndexTable[nibble];
                stepIndex = Math.Clamp(stepIndex, 0, 88);
            }

            return pcmSamples.ToArray();
        }
    }
}
