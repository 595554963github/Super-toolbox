using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Kvs2wav_Converter : BaseExtractor
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

            try
            {
                await Task.Run(() =>
                {
                    var kvsFiles = Directory.GetFiles(directoryPath, "*.kvs", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(directoryPath, "*.kovs", SearchOption.AllDirectories))
                        .OrderBy(f =>
                        {
                            string fileName = Path.GetFileNameWithoutExtension(f);
                            var match = Regex.Match(fileName, @"(\d+)$");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                                return num;
                            return int.MaxValue;
                        })
                        .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                        .ToArray();

                    TotalFilesToConvert = kvsFiles.Length;
                    int successCount = 0;

                    if (kvsFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的KVS文件");
                        OnConversionFailed("未找到需要转换的KVS文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个KVS文件");

                    foreach (var kvsFilePath in kvsFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(kvsFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.kvs");

                        string wavFilePath = Path.Combine(Path.GetDirectoryName(kvsFilePath) ?? "", $"{fileName}.wav");

                        try
                        {
                            if (DecryptKvsToWav(kvsFilePath, wavFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav");
                                OnFileConverted(wavFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.kvs转换失败");
                                OnConversionFailed($"{fileName}.kvs转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.kvs处理错误:{ex.Message}");
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

        private bool DecryptKvsToWav(string kvsPath, string wavPath)
        {
            try
            {
                using var memoryOgg = new MemoryStream();

                var (header, _) = Kvs2ogg_Converter.ReadKvsContainer(kvsPath);

                using var reader = new FileStream(kvsPath, FileMode.Open, FileAccess.Read);
                reader.Seek(Kvs2ogg_Converter.HEADER_SIZE, SeekOrigin.Begin);
                Kvs2ogg_Converter.CopyXorStream(reader, memoryOgg, header.Size);

                memoryOgg.Position = 0;

                using var vorbis = new NVorbis.VorbisReader(memoryOgg);

                int channels = vorbis.Channels;
                int sampleRate = vorbis.SampleRate;
                long totalSamples = vorbis.TotalSamples;

                if (totalSamples <= 0)
                    throw new Exception("无法获取音频时长");

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

                var pcmFormat = new VGAudio.Formats.Pcm16.Pcm16Format(pcmData, sampleRate);
                var audioData = new VGAudio.Formats.AudioData(pcmFormat);

                using var fs = File.Create(wavPath);
                var writer = new VGAudio.Containers.Wave.WaveWriter();
                writer.WriteToStream(audioData, fs);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"转换失败: {ex.Message}");
                return false;
            }
        }
    }
}