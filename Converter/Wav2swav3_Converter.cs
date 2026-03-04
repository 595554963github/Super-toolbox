using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2swav3_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

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

                    if (wavFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的WAV文件");
                        OnConversionFailed("未找到需要转换的WAV文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个WAV文件");

                    foreach (var wavFilePath in wavFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.wav");

                        string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                        string swavFilePath = Path.Combine(fileDirectory, $"{fileName}.swav");

                        try
                        {
                            if (ConvertWavToSwav(wavFilePath, swavFilePath) && File.Exists(swavFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.swav");
                                OnFileConverted(swavFilePath);
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

        private bool ConvertWavToSwav(string wavFilePath, string swavFilePath)
        {
            try
            {
                var wavReader = new WaveReader();
                AudioData audioData;

                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = wavReader.Read(wavStream);
                }

                if (audioData == null)
                    return false;

                var pcmFormat = audioData.GetFormat<Pcm16Format>();
                int sampleRate = pcmFormat.SampleRate;

                short[] monoPcm = MixToMono(pcmFormat);

                return CreateSwavFromPcm16(monoPcm, sampleRate, swavFilePath);
            }
            catch
            {
                return false;
            }
        }

        private bool CreateSwavFromPcm16(short[] pcm16Data, int sampleRate, string swavFilePath)
        {
            try
            {
                byte[] pcm16Bytes = new byte[pcm16Data.Length * 2];
                for (int i = 0; i < pcm16Data.Length; i++)
                {
                    byte[] bytes = BitConverter.GetBytes(pcm16Data[i]);
                    pcm16Bytes[i * 2] = bytes[0];
                    pcm16Bytes[i * 2 + 1] = bytes[1];
                }

                byte[] adpcmData = CompressIMAADPCM(pcm16Bytes);

                uint dataSize = (uint)(adpcmData.Length + 0x14);
                uint fileSize = dataSize + 0x10;

                using (var fs = new FileStream(swavFilePath, FileMode.Create))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("SWAV"));
                    bw.Write((uint)0x0100FEFF);
                    bw.Write(fileSize);
                    bw.Write((ushort)0x10);
                    bw.Write((ushort)0x01);

                    bw.Write(System.Text.Encoding.ASCII.GetBytes("DATA"));
                    bw.Write(dataSize);

                    bw.Write((byte)2);
                    bw.Write((byte)1);
                    bw.Write((ushort)sampleRate);
                    bw.Write((ushort)(16756991 / sampleRate));
                    bw.Write((ushort)1);
                    bw.Write((uint)adpcmData.Length);

                    bw.Write(adpcmData);
                }

                return File.Exists(swavFilePath);
            }
            catch
            {
                return false;
            }
        }

        private byte[] CompressIMAADPCM(byte[] pcm16Bytes)
        {
            List<byte> result = new List<byte>();

            int predictedSample = 0;
            int index = 0;
            int stepsize = stepsizeTable[index];

            int different, newSample, mask, tempStepsize;
            for (int i = 0; i < pcm16Bytes.Length; i += 2)
            {
                short originalSample = BitConverter.ToInt16(pcm16Bytes, i);
                different = originalSample - predictedSample;

                if (different >= 0)
                {
                    newSample = 0;
                }
                else
                {
                    newSample = 8;
                    different = -different;
                }

                mask = 4;
                tempStepsize = stepsize;
                for (int j = 0; j < 3; j++)
                {
                    if (different >= tempStepsize)
                    {
                        newSample |= mask;
                        different -= tempStepsize;
                    }
                    tempStepsize >>= 1;
                    mask >>= 1;
                }

                result.Add((byte)newSample);

                different = 0;
                if ((newSample & 4) != 0)
                    different += stepsize;
                if ((newSample & 2) != 0)
                    different += stepsize >> 1;
                if ((newSample & 1) != 0)
                    different += stepsize >> 2;
                different += stepsize >> 3;

                if ((newSample & 8) != 0)
                    different = -different;
                predictedSample += different;

                if (predictedSample > 32767)
                    predictedSample = 32767;
                else if (predictedSample < -32768)
                    predictedSample = -32768;

                index += indexTable[newSample];
                if (index < 0)
                    index = 0;
                else if (index > 88)
                    index = 88;
                stepsize = stepsizeTable[index];
            }

            return Bit4ToBit8(result.ToArray());
        }
        private byte[] Bit4ToBit8(byte[] bytes)
        {
            List<byte> bit8 = new List<byte>();

            for (int i = 0; i < bytes.Length; i += 2)
            {
                byte byte1 = bytes[i];
                byte byte2 = 0;
                if (i + 1 < bytes.Length)
                    byte2 = (byte)(bytes[i + 1] << 4);
                bit8.Add((byte)(byte1 + byte2));
            }

            return bit8.ToArray();
        }

        private int[] indexTable = new int[16] { -1, -1, -1, -1, 2, 4, 6, 8,
                                             -1, -1, -1, -1, 2, 4, 6, 8 };

        private int[] stepsizeTable = new int[89] { 7, 8, 9, 10, 11, 12, 13, 14,
                                                16, 17, 19, 21, 23, 25, 28,
                                                31, 34, 37, 41, 45, 50, 55,
                                                60, 66, 73, 80, 88, 97, 107,
                                                118, 130, 143, 157, 173, 190, 209,
                                                230, 253, 279, 307, 337, 371, 408,
                                                449, 494, 544, 598, 658, 724, 796,
                                                876, 963, 1060, 1166, 1282, 1411, 1552,
                                                1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026,
                                                4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630,
                                                9493, 10442, 11487, 12635, 13899, 15289, 16818,
                                                18500, 20350, 22385, 24623, 27086, 29794, 32767 };

        private short[] MixToMono(Pcm16Format pcmFormat)
        {
            short[][] channels = pcmFormat.Channels;
            int channelCount = channels.Length;
            int sampleCount = channels[0].Length;

            if (channelCount == 1)
                return channels[0];

            short[] mono = new short[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int sum = 0;
                for (int ch = 0; ch < channelCount; ch++)
                {
                    sum += channels[ch][i];
                }
                mono[i] = (short)(sum / channelCount);
            }

            return mono;
        }
    }
}