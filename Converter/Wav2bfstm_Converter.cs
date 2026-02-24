using VGAudio.Containers.NintendoWare;
using VGAudio.Containers.Wave;
using VGAudio.Formats;

namespace super_toolbox
{
    public class Wav2bfstm_Converter : BaseExtractor
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

            var wavFiles = Directory.GetFiles(directoryPath, "*.wav", SearchOption.AllDirectories);
            TotalFilesToConvert = wavFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.wav");

                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;

                    try
                    {
                        string bfstmFile = Path.Combine(fileDirectory, $"{fileName}.bfstm");

                        if (File.Exists(bfstmFile))
                            File.Delete(bfstmFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToBfstm(wavFilePath, bfstmFile, cancellationToken));

                        if (conversionSuccess && File.Exists(bfstmFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(bfstmFile)}");
                            OnFileConverted(bfstmFile);
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

        private bool ConvertWavToBfstm(string wavFilePath, string bfstmFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取WAV文件:{Path.GetFileName(wavFilePath)}");

                var waveReader = new WaveReader();
                AudioData audioData;

                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = waveReader.Read(wavStream);
                }

                if (audioData == null)
                {
                    throw new InvalidOperationException("无法读取WAV音频数据");
                }

                ConversionProgress?.Invoke(this, $"转换为BFSTM格式:{Path.GetFileName(bfstmFilePath)}");

                var writer = new BCFstmWriter(NwTarget.Cafe);
                writer.Configuration = new BxstmConfiguration
                {
                    Codec = NwCodec.Pcm16Bit,
                    SamplesPerInterleave = 0x2000,
                    SamplesPerSeekTableEntry = 0x2000,
                    LoopPointAlignment = 1,
                    RecalculateSeekTable = true,
                    RecalculateLoopContext = true
                };

                using (var bfstmStream = File.Create(bfstmFilePath))
                {
                    writer.WriteToStream(audioData, bfstmStream, writer.Configuration);
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