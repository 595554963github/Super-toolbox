using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2asf3_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private async Task<bool> ConvertWAVToASF(string wavFilePath, string asfFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取WAV文件:{Path.GetFileName(wavFilePath)}");

                var wavReader = new WaveReader();
                AudioData audioData;
                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = wavReader.Read(wavStream);
                }

                if (audioData == null)
                {
                    ConversionError?.Invoke(this, "无法读取WAV音频数据");
                    return false;
                }

                Pcm16Format pcm16 = audioData.GetFormat<Pcm16Format>();
                if (pcm16 == null)
                {
                    var allFormats = audioData.GetAllFormats().ToList();
                    if (allFormats.Count > 0)
                    {
                        pcm16 = allFormats.First().ToPcm16();
                    }
                    else
                    {
                        ConversionError?.Invoke(this, "无法转换WAV格式");
                        return false;
                    }
                }

                int nChannels = pcm16.ChannelCount;
                int nSamples = pcm16.SampleCount;
                short[][] channels = pcm16.Channels;

                return await Task.Run(() =>
                {
                    using var outFile = File.Create(asfFilePath);
                    var encoder = new AsfEncoder(3, nChannels, pcm16.SampleRate, nSamples);
                    encoder.WriteFile(outFile, channels);
                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                return false;
            }
        }

        public async Task ExtractSingleAsync(string wavFilePath, string outputPath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(wavFilePath))
            {
                ConversionError?.Invoke(this, $"源文件{wavFilePath}不存在");
                OnConversionFailed($"源文件{wavFilePath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理:{Path.GetFileName(wavFilePath)}");

            try
            {
                bool success = await ConvertWAVToASF(wavFilePath, outputPath, cancellationToken);

                if (success && File.Exists(outputPath))
                {
                    ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(outputPath)}");
                    OnFileConverted(outputPath);
                    OnConversionCompleted();
                }
                else
                {
                    ConversionError?.Invoke(this, $"{Path.GetFileName(wavFilePath)}转换失败");
                    OnConversionFailed($"{Path.GetFileName(wavFilePath)}转换失败");
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                OnConversionFailed($"{Path.GetFileName(wavFilePath)}处理错误:{ex.Message}");
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
                    return match.Success && int.TryParse(match.Groups[1].Value, out int num) ? num : int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = wavFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.wav");

                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                    string asfFilePath = Path.Combine(fileDirectory, $"{fileName}.asf");

                    try
                    {
                        bool conversionSuccess = await ConvertWAVToASF(wavFilePath, asfFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(asfFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(asfFilePath)}");
                            OnFileConverted(asfFilePath);
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