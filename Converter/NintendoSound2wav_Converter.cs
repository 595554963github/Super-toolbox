using VGAudio.Containers.NintendoWare;
using VGAudio.Containers.Wave;
using VGAudio.Formats;

namespace super_toolbox
{
    public class NintendoSound2wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private readonly string[] _supportedExtensions = new[]
        {
            "*.bcstm", "*.bfstm", "*.brstm",
            "*.bcwav", "*.bfwav", "*.brwav"
        };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var allFiles = new List<string>();
            foreach (var pattern in _supportedExtensions)
            {
                allFiles.AddRange(Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories));
            }

            TotalFilesToConvert = allFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var inputFilePath in allFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string extension = Path.GetExtension(inputFilePath).ToLower();
                    string fileName = Path.GetFileNameWithoutExtension(inputFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}{extension}");

                    string fileDirectory = Path.GetDirectoryName(inputFilePath) ?? string.Empty;

                    try
                    {
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertToWav(inputFilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}{extension}转换失败");
                            OnConversionFailed($"{fileName}{extension}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}{extension}处理错误:{ex.Message}");
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

        private bool ConvertToWav(string inputFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                string extension = Path.GetExtension(inputFilePath).ToLower();
                ConversionProgress?.Invoke(this, $"读取{extension.ToUpper()}文件:{Path.GetFileName(inputFilePath)}");

                AudioData audioData;

                using (var inputStream = File.OpenRead(inputFilePath))
                {
                    switch (extension)
                    {
                        case ".brstm":
                            var brstmReader = new BrstmReader();
                            audioData = brstmReader.Read(inputStream);
                            break;
                        case ".brwav":
                            var brwavReader = new BrwavReader();
                            audioData = brwavReader.Read(inputStream);
                            break;
                        default:
                            var bcfstmReader = new BCFstmReader();
                            audioData = bcfstmReader.Read(inputStream);
                            break;
                    }
                }

                if (audioData == null)
                {
                    throw new InvalidOperationException($"无法读取{extension.ToUpper()}音频数据");
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