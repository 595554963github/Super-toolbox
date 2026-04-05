using NVorbis;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Ogg2wav_Converter : BaseExtractor
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

            var oggFiles = Directory.GetFiles(directoryPath, "*.ogg", SearchOption.AllDirectories)
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

            TotalFilesToConvert = oggFiles.Length;
            int successCount = 0;

            if (oggFiles.Length == 0)
            {
                ConversionError?.Invoke(this, "未找到OGG文件");
                OnConversionFailed("未找到OGG文件");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个OGG文件");

            foreach (var oggFile in oggFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);

                string fileName = Path.GetFileNameWithoutExtension(oggFile);
                ConversionProgress?.Invoke(this, $"正在转换:{fileName}.ogg");

                string wavFile = Path.ChangeExtension(oggFile, ".wav");

                try
                {
                    if (File.Exists(wavFile)) File.Delete(wavFile);

                    await Task.Run(() =>
                    {
                        using (var vorbis = new VorbisReader(oggFile))
                        {
                            int channels = vorbis.Channels;
                            int sampleRate = vorbis.SampleRate;
                            long totalSamples = vorbis.TotalSamples;

                            if (totalSamples <= 0)
                            {
                                throw new Exception("无法获取音频时长");
                            }

                            float[] buffer = new float[totalSamples * channels];
                            vorbis.ReadSamples(buffer, 0, buffer.Length);

                            short[][] pcmData = new short[channels][];
                            for (int ch = 0; ch < channels; ch++)
                            {
                                pcmData[ch] = new short[totalSamples];
                                for (int i = 0; i < totalSamples; i++)
                                {
                                    float sample = buffer[i * channels + ch];
                                    if (sample > 1.0f) sample = 1.0f;
                                    if (sample < -1.0f) sample = -1.0f;
                                    pcmData[ch][i] = (short)(sample * 32767.0f);
                                }
                            }

                            var pcmFormat = new Pcm16Format(pcmData, sampleRate);
                            var audioData = new AudioData(pcmFormat);

                            using (var fs = File.Create(wavFile))
                            {
                                var writer = new WaveWriter();
                                writer.WriteToStream(audioData, fs);
                            }
                        }
                    }, cancellationToken);

                    if (File.Exists(wavFile) && new FileInfo(wavFile).Length > 0)
                    {
                        successCount++;
                        ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav");
                        OnFileConverted(wavFile);
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"{fileName}.ogg转换失败");
                        OnConversionFailed($"{fileName}.ogg转换失败");
                    }
                }
                catch (Exception ex)
                {
                    ConversionError?.Invoke(this, $"{fileName}.ogg转换异常:{ex.Message}");
                    OnConversionFailed($"{fileName}.ogg处理错误:{ex.Message}");
                }
            }

            ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
            OnConversionCompleted();
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}