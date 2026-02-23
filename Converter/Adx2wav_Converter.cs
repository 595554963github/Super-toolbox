using VGAudio.Containers.Adx;
using VGAudio.Containers.Wave;
using VGAudio.Formats;

namespace super_toolbox
{
    public class Adx2wav_Converter : BaseExtractor
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

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var adxFiles = Directory.GetFiles(directoryPath, "*.adx", SearchOption.AllDirectories);
            TotalFilesToConvert = adxFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var adxFilePath in adxFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(adxFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.adx");

                    string fileDirectory = Path.GetDirectoryName(adxFilePath) ?? string.Empty;

                    try
                    {
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertAdxToWav(adxFilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.adx转换失败");
                            OnConversionFailed($"{fileName}.adx转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.adx处理错误:{ex.Message}");
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

        private bool ConvertAdxToWav(string adxFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取ADX文件:{Path.GetFileName(adxFilePath)}");

                var adxReader = new AdxReader();
                AudioData audioData;

                using (var adxStream = File.OpenRead(adxFilePath))
                {
                    audioData = adxReader.Read(adxStream);
                }

                if (audioData == null)
                {
                    throw new InvalidOperationException("无法读取ADX音频数据");
                }

                ConversionProgress?.Invoke(this, $"转换为WAV格式:{Path.GetFileName(wavFilePath)}");

                var waveConfig = new WaveConfiguration
                {
                    Codec = WaveCodec.Pcm16Bit
                };

                var waveWriter = new WaveWriter();

                using (var waveStream = File.Create(wavFilePath))
                {
                    waveWriter.WriteToStream(audioData, waveStream, waveConfig);
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