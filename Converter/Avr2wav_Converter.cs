namespace super_toolbox
{
    public class Avr2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private void OnConversionStarted(string message) => ConversionStarted?.Invoke(this, message);
        private void OnConversionProgress(string message) => ConversionProgress?.Invoke(this, message);
        private void OnConversionError(string message) => ConversionError?.Invoke(this, message);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnConversionError($"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            OnConversionStarted($"开始处理目录:{directoryPath}");

            var files = Directory.GetFiles(directoryPath, "*.avr", SearchOption.AllDirectories)
                .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = files.Length;
            int successCount = 0;

            foreach (var filePath in files)
            {
                ThrowIfCancellationRequested(cancellationToken);

                string fileName = Path.GetFileNameWithoutExtension(filePath);
                OnConversionProgress($"正在处理:{fileName}");

                string wavFilePath = Path.ChangeExtension(filePath, ".wav");

                try
                {
                    if (File.Exists(wavFilePath)) File.Delete(wavFilePath);

                    bool success = await ConvertWithCodec(filePath, wavFilePath, cancellationToken);

                    if (success && File.Exists(wavFilePath))
                    {
                        successCount++;
                        OnConversionProgress($"转换成功:{Path.GetFileName(wavFilePath)}");
                        OnFileConverted(wavFilePath);
                    }
                    else
                    {
                        OnConversionError($"{fileName}转换失败");
                        OnConversionFailed($"{fileName}转换失败");
                    }
                }
                catch (Exception ex)
                {
                    OnConversionError($"转换异常:{ex.Message}");
                    OnConversionFailed($"{fileName}处理错误:{ex.Message}");
                }
            }

            if (successCount > 0)
                OnConversionProgress($"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
            else
                OnConversionProgress("转换完成,但未成功转换任何文件");

            OnConversionCompleted();
        }
    }
}