using QOALib;

namespace super_toolbox
{
    public class Qoa2Wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static bool ConvertQoaToWav(string inputQoaFile, string outputWavFile)
        {
            try
            {
                using (FileStream qoaStream = File.OpenRead(inputQoaFile))
                using (FileStream wavStream = File.Create(outputWavFile))
                {
                    QOA qoaDecoder = new QOA();
                    qoaDecoder.DecodeToWav(qoaStream, wavStream);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"转换错误:{ex.Message}");
                return false;
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> convertedFiles = new List<string>();
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var qoaFiles = Directory.EnumerateFiles(directoryPath, "*.qoa", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = qoaFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var qoaFilePath in qoaFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(qoaFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(qoaFilePath);
                    string fileDirectory = Path.GetDirectoryName(qoaFilePath) ?? string.Empty;
                    fileName = fileName.Replace(".wav", "", StringComparison.OrdinalIgnoreCase);

                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        bool conversionSuccess = await Task.Run(() => ConvertQoaToWav(qoaFilePath, wavFilePath), cancellationToken);

                        if (conversionSuccess && File.Exists(wavFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(wavFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFilePath)}");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.qoa转换失败");
                            OnConversionFailed($"{fileName}.qoa转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.qoa处理错误:{ex.Message}");
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

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}