namespace super_toolbox
{
    public class Wav2qoa_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static bool ConvertWavToQoa(string inputWavFile, string outputQoaFile)
        {
            try
            {
                using (FileStream wavStream = File.OpenRead(inputWavFile))
                using (MemoryStream wavMemoryStream = new MemoryStream())
                {
                    wavStream.CopyTo(wavMemoryStream);
                    wavMemoryStream.Position = 0;

                    using (FileStream qoaStream = File.Create(outputQoaFile))
                    {
                        QOALib.QOA qoaEncoder = new QOALib.QOA();
                        qoaEncoder.EncodeWAVToQOA(wavMemoryStream, qoaStream);
                        return true;
                    }
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
            var wavFiles = Directory.EnumerateFiles(directoryPath, "*.wav", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = wavFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(wavFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                    fileName = fileName.Replace(".qoa", "", StringComparison.OrdinalIgnoreCase);

                    string qoaFilePath = Path.Combine(fileDirectory, $"{fileName}.qoa");

                    try
                    {
                        bool conversionSuccess = await Task.Run(() => ConvertWavToQoa(wavFilePath, qoaFilePath), cancellationToken);

                        if (conversionSuccess && File.Exists(qoaFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(qoaFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(qoaFilePath)}");
                            OnFileConverted(qoaFilePath);
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

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}

