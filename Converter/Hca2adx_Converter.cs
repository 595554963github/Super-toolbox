using VGAudio.Containers.Adx;
using VGAudio.Containers.Hca;
using VGAudio.Formats;

namespace super_toolbox
{
    public class Hca2adx_Converter : BaseExtractor
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

            var hcaFiles = Directory.GetFiles(directoryPath, "*.hca", SearchOption.AllDirectories);
            TotalFilesToConvert = hcaFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var hcaFilePath in hcaFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(hcaFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.hca");

                    string fileDirectory = Path.GetDirectoryName(hcaFilePath) ?? string.Empty;

                    try
                    {
                        string adxFile = Path.Combine(fileDirectory, $"{fileName}.adx");

                        if (File.Exists(adxFile))
                            File.Delete(adxFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertHcaToAdx(hcaFilePath, adxFile, cancellationToken));

                        if (conversionSuccess && File.Exists(adxFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(adxFile)}");
                            OnFileConverted(adxFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.hca转换失败");
                            OnConversionFailed($"{fileName}.hca转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.hca处理错误:{ex.Message}");
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

        private bool ConvertHcaToAdx(string hcaFilePath, string adxFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取HCA文件:{Path.GetFileName(hcaFilePath)}");

                var hcaReader = new HcaReader();
                AudioData audioData;

                using (var hcaStream = File.OpenRead(hcaFilePath))
                {
                    audioData = hcaReader.Read(hcaStream);
                }

                if (audioData == null)
                {
                    throw new InvalidOperationException("无法读取HCA音频数据");
                }

                ConversionProgress?.Invoke(this, $"转换为ADX格式:{Path.GetFileName(adxFilePath)}");

                var adxConfig = new AdxConfiguration
                {
                    Version = 4
                };

                var adxWriter = new AdxWriter();

                using (var adxStream = File.Create(adxFilePath))
                {
                    adxWriter.WriteToStream(audioData, adxStream, adxConfig);
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