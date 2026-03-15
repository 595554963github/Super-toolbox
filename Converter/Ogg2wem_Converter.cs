using System.Text.RegularExpressions;
using NVorbis;

namespace super_toolbox
{
    public class Ogg2wem_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public VorbisQuality Quality { get; set; } = VorbisQuality.High;
        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public float? Volume { get; set; }

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
                        ConversionError?.Invoke(this, "未找到需要转换的OGG文件");
                        OnConversionFailed("未找到需要转换的OGG文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个OGG文件");

                    foreach (var oggFilePath in oggFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(oggFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.ogg");

                        string fileDirectory = Path.GetDirectoryName(oggFilePath) ?? string.Empty;
                        string wemFilePath = Path.Combine(fileDirectory, $"{fileName}.wem");

                        try
                        {
                            if (ConvertOggToWem(oggFilePath, wemFilePath) && File.Exists(wemFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.wem");
                                OnFileConverted(wemFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.ogg转换失败");
                                OnConversionFailed($"{fileName}.ogg转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.ogg处理错误:{ex.Message}");
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

        private short[] ResampleSamples(short[] samples, int originalRate, int targetRate)
        {
            if (originalRate == targetRate)
                return samples;

            double ratio = (double)targetRate / originalRate;
            int newLength = (int)(samples.Length * ratio);
            short[] result = new short[newLength];

            for (int i = 0; i < newLength; i++)
            {
                double pos = i / ratio;
                int index = (int)pos;
                double frac = pos - index;

                if (index >= samples.Length - 1)
                {
                    result[i] = samples[samples.Length - 1];
                }
                else
                {
                    double sample = samples[index] * (1 - frac) + samples[index + 1] * frac;
                    result[i] = (short)sample;
                }
            }

            return result;
        }

        private short[] ConvertChannels(float[] samples, int sourceChannels, int targetChannels)
        {
            int frameCount = samples.Length / sourceChannels;
            short[] result = new short[frameCount * targetChannels];

            for (int i = 0; i < frameCount; i++)
            {
                for (int j = 0; j < targetChannels; j++)
                {
                    int sourceIndex = i * sourceChannels + (j % sourceChannels);
                    float sample = samples[sourceIndex] * (Volume ?? 1.0f);
                    if (sample > 1.0f) sample = 1.0f;
                    if (sample < -1.0f) sample = -1.0f;
                    result[i * targetChannels + j] = (short)(sample * 32767);
                }
            }

            return result;
        }

        private bool ConvertOggToWem(string oggPath, string wemPath)
        {
            try
            {
                using var vorbis = new VorbisReader(oggPath);

                float[] buffer = new float[vorbis.TotalSamples * vorbis.Channels];
                vorbis.ReadSamples(buffer, 0, buffer.Length);

                int targetSampleRate = SampleRate ?? vorbis.SampleRate;
                int targetChannels = Channels ?? vorbis.Channels;

                short[] pcm = ConvertChannels(buffer, vorbis.Channels, targetChannels);

                if (targetSampleRate != vorbis.SampleRate)
                {
                    pcm = ResampleSamples(pcm, vorbis.SampleRate, targetSampleRate);
                }

                int bitrate = Quality switch
                {
                    VorbisQuality.High => 192000,
                    VorbisQuality.Medium => 128000,
                    VorbisQuality.Low => 64000,
                    _ => 128000
                };

                string? parentDir = Path.GetDirectoryName(wemPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    Directory.CreateDirectory(parentDir);

                using var fs = new FileStream(wemPath, FileMode.Create);
                using var writer = new BinaryWriter(fs);

                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(0);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((byte)0xFE);
                writer.Write((byte)0xFF);
                writer.Write((short)targetChannels);
                writer.Write(targetSampleRate);
                writer.Write(bitrate);
                writer.Write((short)(targetChannels * 2));
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(0);

                long dataPos = fs.Position;

                foreach (short sample in pcm)
                {
                    writer.Write(sample);
                }

                long fileLength = fs.Length;
                fs.Seek(4, SeekOrigin.Begin);
                writer.Write((int)(fileLength - 8));
                fs.Seek((int)dataPos - 4, SeekOrigin.Begin);
                writer.Write((int)(fileLength - dataPos));

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    public enum VorbisQuality
    {
        High,
        Medium,
        Low
    }
}