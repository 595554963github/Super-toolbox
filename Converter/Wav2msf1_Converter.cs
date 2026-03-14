using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2msf1_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}(大端序MSF)");

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
                        string msfFile = Path.Combine(fileDirectory, $"{fileName}.msf");

                        if (File.Exists(msfFile))
                            File.Delete(msfFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToMsfBigEndian(wavFilePath, msfFile, cancellationToken));

                        if (conversionSuccess && File.Exists(msfFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(msfFile)}");
                            OnFileConverted(msfFile);
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

        private bool ConvertWavToMsfBigEndian(string wavFilePath, string msfFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取WAV文件:{Path.GetFileName(wavFilePath)}");

                var waveReader = new WaveReader();
                AudioData audioData;

                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = waveReader.Read(wavStream);
                }

                if (audioData == null)
                {
                    throw new InvalidOperationException("无法读取WAV音频数据");
                }

                var pcmFormat = audioData.GetFormat<Pcm16Format>();
                if (pcmFormat == null)
                {
                    throw new InvalidOperationException("不支持的WAV格式,需要16-bit PCM");
                }

                ConversionProgress?.Invoke(this, $"转换为MSF格式(大端序):{Path.GetFileName(msfFilePath)}");

                byte[] msfData = CreateMsfBigEndian(pcmFormat);
                File.WriteAllBytes(msfFilePath, msfData);

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

        private byte[] CreateMsfBigEndian(Pcm16Format pcmFormat)
        {
            byte[] header = new byte[64];

            header[0] = 0x4D;
            header[1] = 0x53;
            header[2] = 0x46;
            header[3] = 0x43;

            int channelCount = pcmFormat.ChannelCount;
            short[] samples = pcmFormat.Channels[0];
            int sampleCount = samples.Length * channelCount;

            header[4] = 0x00;
            header[5] = 0x00;
            header[6] = 0x00;
            header[7] = 0x00;

            header[8] = (byte)(channelCount >> 24);
            header[9] = (byte)(channelCount >> 16);
            header[10] = (byte)(channelCount >> 8);
            header[11] = (byte)channelCount;

            int dataSize = sampleCount * 2;
            header[12] = (byte)(dataSize >> 24);
            header[13] = (byte)(dataSize >> 16);
            header[14] = (byte)(dataSize >> 8);
            header[15] = (byte)dataSize;

            int sampleRate = pcmFormat.SampleRate;
            header[16] = (byte)(sampleRate >> 24);
            header[17] = (byte)(sampleRate >> 16);
            header[18] = (byte)(sampleRate >> 8);
            header[19] = (byte)sampleRate;

            header[20] = 0x00;
            header[21] = 0x00;
            header[22] = 0x00;
            header[23] = 0x10;

            for (int i = 24; i < 32; i++)
            {
                header[i] = 0x00;
            }

            for (int i = 32; i < 64; i++)
            {
                header[i] = 0xFF;
            }

            byte[] audioData = new byte[dataSize];
            int index = 0;

            if (channelCount == 2)
            {
                short[] leftChannel = pcmFormat.Channels[0];
                short[] rightChannel = pcmFormat.Channels[1];

                for (int i = 0; i < leftChannel.Length; i++)
                {
                    short leftSample = leftChannel[i];
                    audioData[index++] = (byte)(leftSample >> 8);
                    audioData[index++] = (byte)leftSample;

                    short rightSample = rightChannel[i];
                    audioData[index++] = (byte)(rightSample >> 8);
                    audioData[index++] = (byte)rightSample;
                }
            }
            else
            {
                short[] monoChannel = pcmFormat.Channels[0];

                for (int i = 0; i < monoChannel.Length; i++)
                {
                    short sample = monoChannel[i];
                    audioData[index++] = (byte)(sample >> 8);
                    audioData[index++] = (byte)sample;
                }
            }

            byte[] result = new byte[header.Length + audioData.Length];
            Array.Copy(header, 0, result, 0, header.Length);
            Array.Copy(audioData, 0, result, header.Length, audioData.Length);

            return result;
        }
    }
}