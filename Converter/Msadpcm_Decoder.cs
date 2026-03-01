namespace super_toolbox
{
    public class Msadpcm_Decoder : BaseExtractor
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

            try
            {
                await Task.Run(() =>
                {
                    var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsMsadpcmFile(f))
                        .OrderBy(f =>
                        {
                            string fileName = Path.GetFileNameWithoutExtension(f);
                            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"_(\d+)$");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                                return num;
                            return int.MaxValue;
                        })
                        .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                        .ToArray();

                    TotalFilesToConvert = files.Length;
                    int successCount = 0;

                    if (files.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到MS-ADPCM格式的文件");
                        OnConversionFailed("未找到MS-ADPCM格式的文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始解码MS-ADPCM到WAV,共{TotalFilesToConvert}个文件");

                    foreach (var filePath in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string fileExt = Path.GetExtension(filePath);
                        ConversionProgress?.Invoke(this, $"正在解码:{fileName}{fileExt}");

                        string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                        string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                        try
                        {
                            if (File.Exists(wavFilePath))
                                File.Delete(wavFilePath);

                            if (ConvertToWav(filePath, wavFilePath) && File.Exists(wavFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav");
                                OnFileConverted(wavFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}{fileExt}解码失败");
                                OnConversionFailed($"{fileName}{fileExt}解码失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"解码异常:{ex.Message}");
                            OnConversionFailed($"{fileName}{fileExt}处理错误:{ex.Message}");
                        }
                    }

                    ConversionProgress?.Invoke(this, $"解码完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "解码操作已取消");
                OnConversionFailed("解码操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码失败:{ex.Message}");
                OnConversionFailed($"解码失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private bool IsMsadpcmFile(string filePath)
        {
            try
            {
                byte[] buffer = new byte[0x18];
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 0x18)
                        return false;
                    fs.Read(buffer, 0, 0x18);
                }

                if (buffer[0] != 0x52 || buffer[1] != 0x49 || buffer[2] != 0x46 || buffer[3] != 0x46)
                    return false;

                Span<byte> magic = new Span<byte>(buffer, 0x10, 8);

                return magic.SequenceEqual(new byte[] { 0x32, 0x00, 0x00, 0x00, 0x02, 0x00, 0x02, 0x00 });
            }
            catch
            {
                return false;
            }
        }

        private bool ConvertToWav(string inputFile, string wavFilePath)
        {
            try
            {
                File.Copy(inputFile, wavFilePath, true);
                return true;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码过程异常:{ex.Message}");
                return false;
            }
        }
    }
}