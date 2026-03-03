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
                            byte[] swavData = File.ReadAllBytes(swavFilePath);

                            if (swavData.Length < 0x18 ||
                                BitConverter.ToUInt32(swavData, 0x00) != 0x56415753 ||
                                BitConverter.ToUInt16(swavData, 0x04) != 0xFEFF ||
                                BitConverter.ToUInt16(swavData, 0x0C) != 0x0010 ||
                                BitConverter.ToUInt32(swavData, 0x10) != 0x41544144)
                            {
                                throw new InvalidDataException("无效的SWAV文件格式");
                            }

                            byte[] sampData = new byte[swavData.Length - 0x18];
                            Array.Copy(swavData, 0x18, sampData, 0, sampData.Length);

                            var swavInfo = ParseSampHeader(sampData);

                            short[] pcmData = DecodeSamples(swavInfo);

                            short[][] stereoChannels = new short[2][];
                            stereoChannels[0] = pcmData;
                            stereoChannels[1] = new short[pcmData.Length];
                            Array.Copy(pcmData, stereoChannels[1], pcmData.Length);

                            var stereoFormat = new Pcm16Format(stereoChannels, swavInfo.Rate);
                            var wavWriter = new WaveWriter();

                            using (var outFs = File.Create(outputWavPath))
                            {
                                wavWriter.WriteToStream(stereoFormat, outFs);
                            }

                            successCount++;
                            ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav(立体声)");
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

        private SwavInfo ParseSampHeader(byte[] data)
        {
            if (data.Length < 0x0C)
                throw new InvalidDataException("无效的SAMP头");

            var info = new SwavInfo();

            info.WaveType = data[0x00];
            info.HasLoop = data[0x01] != 0;
            info.Rate = BitConverter.ToUInt16(data, 0x02);
            info.Time = BitConverter.ToUInt16(data, 0x04);

            int loopStartInBytes = BitConverter.ToUInt16(data, 0x06) * 4;
            int loopLenInBytes = BitConverter.ToInt32(data, 0x08) * 4;

            int firstSampOfs = info.WaveType == WAVE_ADPCM ? 4 : 0;
            int byteToSampMul = info.WaveType == WAVE_ADPCM ? 2 : 1;
            int byteToSampDiv = info.WaveType == WAVE_PCM16 ? 2 : 1;

            int loopStart = (loopStartInBytes - firstSampOfs) * byteToSampMul / byteToSampDiv;
            int loopLen = loopLenInBytes * byteToSampMul / byteToSampDiv;

            info.LoopStart = info.HasLoop ? loopStart : 0;
            info.NumSamples = loopStart + loopLen;
            info.DataOffset = 0x0C;
            info.DataSize = loopStartInBytes + loopLenInBytes;

            if (info.DataOffset + info.DataSize > data.Length)
                throw new InvalidDataException("SAMP数据长度不足");

            info.Data = new byte[info.DataSize];
            Array.Copy(data, info.DataOffset, info.Data, 0, info.DataSize);

            return info;
        }

        private short[] DecodeSamples(SwavInfo info)
        {
            int bps = info.WaveType == WAVE_PCM8 ? 8 : 16;
            short[] result = new short[info.NumSamples];

            if (info.WaveType == WAVE_PCM8)
            {
                for (int i = 0; i < info.NumSamples && i < info.DataSize; i++)
                {
                    result[i] = (short)((info.Data[i] ^ 0x80) << 8);
                }
            }
            else if (info.WaveType == WAVE_PCM16)
            {
                for (int i = 0; i < info.NumSamples && i * 2 + 1 < info.DataSize; i++)
                {
                    result[i] = BitConverter.ToInt16(info.Data, i * 2);
                }
            }
            else if (info.WaveType == WAVE_ADPCM)
            {
                int sample = BitConverter.ToInt16(info.Data, 0);
                int stepIndex = info.Data[2];
                bool low = true;
                int dataPos = 4;

                for (int i = 0; i < info.NumSamples; i++)
                {
                    result[i] = (short)sample;

                    if (dataPos >= info.DataSize)
                        break;

                    byte code = info.Data[dataPos];

                    if (!low)
                    {
                        code >>= 4;
                        dataPos++;
                    }

                    code &= 0x0F;

                    int step = ADPCMTable[stepIndex];
                    int diff = step >> 3;

                    if ((code & 1) != 0) diff += step >> 2;
                    if ((code & 2) != 0) diff += step >> 1;
                    if ((code & 4) != 0) diff += step;

                    if ((code & 8) != 0)
                    {
                        sample -= diff;
                        if (sample < -32767) sample = -32767;
                    }
                    else
                    {
                        sample += diff;
                        if (sample > 32767) sample = 32767;
                    }

                    stepIndex += IMAIndexTable[code];
                    if (stepIndex < 0) stepIndex = 0;
                    if (stepIndex > 88) stepIndex = 88;

                    low = !low;
                }
            }

            return result;
        }

        private class SwavInfo
        {
            public int WaveType { get; set; }
            public bool HasLoop { get; set; }
            public int Rate { get; set; }
            public int Time { get; set; }
            public int LoopStart { get; set; }
            public int NumSamples { get; set; }
            public int DataOffset { get; set; }
            public int DataSize { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }
    }
}
