using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Msf2wav_Converter : BaseExtractor
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

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var msfFiles = Directory.GetFiles(directoryPath, "*.msf", SearchOption.AllDirectories)
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

            TotalFilesToConvert = msfFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var msfFilePath in msfFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(msfFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.msf");

                    string fileDirectory = Path.GetDirectoryName(msfFilePath) ?? string.Empty;

                    try
                    {
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertMsfToWav(msfFilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.msf转换失败");
                            OnConversionFailed($"{fileName}.msf转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.msf处理错误:{ex.Message}");
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

        private bool ConvertMsfToWav(string msfFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取MSF文件:{Path.GetFileName(msfFilePath)}");

                byte[] msfData = File.ReadAllBytes(msfFilePath);

                if (msfData.Length < 64)
                {
                    throw new InvalidOperationException("无效的MSF文件");
                }

                if (msfData[0] != 0x4D || msfData[1] != 0x53 || msfData[2] != 0x46)
                {
                    throw new InvalidOperationException("不是有效的MSF文件");
                }

                int codec = (msfData[4] << 24) | (msfData[5] << 16) | (msfData[6] << 8) | msfData[7];

                int channelCount = (msfData[8] << 24) | (msfData[9] << 16) | (msfData[10] << 8) | msfData[11];

                int dataSize = (msfData[12] << 24) | (msfData[13] << 16) | (msfData[14] << 8) | msfData[15];

                int sampleRate = (msfData[16] << 24) | (msfData[17] << 16) | (msfData[18] << 8) | msfData[19];

                if (codec != 0 && codec != 1)
                {
                    throw new InvalidOperationException($"不支持的codec:{codec},仅支持PCM16");
                }

                int sampleCount = dataSize / 2;
                short[] samples = new short[sampleCount];

                int audioStart = 64;

                if (codec == 0)
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int offset = audioStart + (i * 2);
                        samples[i] = (short)((msfData[offset] << 8) | msfData[offset + 1]);
                    }
                }
                else
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int offset = audioStart + (i * 2);
                        samples[i] = (short)(msfData[offset] | (msfData[offset + 1] << 8));
                    }
                }

                short[][] channelSamples = new short[channelCount][];

                if (channelCount == 2)
                {
                    int frameCount = sampleCount / 2;
                    short[] leftChannel = new short[frameCount];
                    short[] rightChannel = new short[frameCount];

                    for (int i = 0; i < frameCount; i++)
                    {
                        leftChannel[i] = samples[i * 2];
                        rightChannel[i] = samples[i * 2 + 1];
                    }

                    channelSamples[0] = leftChannel;
                    channelSamples[1] = rightChannel;
                }
                else
                {
                    channelSamples[0] = samples;
                }

                var pcmFormat = new Pcm16Format(channelSamples, sampleRate);

                var waveWriter = new WaveWriter();

                using (var wavStream = File.Create(wavFilePath))
                {
                    waveWriter.WriteToStream(pcmFormat, wavStream);
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
    }
}