using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2swav1_Converter : BaseExtractor
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
                byte[] pcm8Data = new byte[pcm16Data.Length];
                for (int i = 0; i < pcm16Data.Length; i++)
                {
                    pcm8Data[i] = (byte)((pcm16Data[i] >> 8) & 0xFF);
                }

                uint dataSize = (uint)(pcm8Data.Length + 0x14);
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

                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)sampleRate);
                    bw.Write((ushort)(16756991 / sampleRate));
                    bw.Write((ushort)0);
                    bw.Write((uint)pcm8Data.Length);

                    bw.Write(pcm8Data);
                }

                return File.Exists(swavFilePath);
            }
            catch
            {
                return false;
            }
        }

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
