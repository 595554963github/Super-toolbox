using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Msu2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var msuFiles = Directory.GetFiles(directoryPath, "*.raw", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = msuFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var msuFilePath in msuFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fileName = Path.GetFileNameWithoutExtension(msuFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.pcm");

                    string fileDirectory = Path.GetDirectoryName(msuFilePath) ?? string.Empty;

                    try
                    {
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertMsuToWav(msuFilePath, wavFile, cancellationToken), cancellationToken);

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.pcm转换失败");
                            OnConversionFailed($"{fileName}.pcm转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.pcm处理错误:{ex.Message}");
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

        private bool ConvertMsuToWav(string msuFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] msuData = File.ReadAllBytes(msuFilePath);

                using (var memoryStream = new MemoryStream())
                {
                    string headerStr = System.Text.Encoding.ASCII.GetString(msuData, 0, 4);
                    if (headerStr != "MSU1")
                    {
                        ConversionError?.Invoke(this, "无效的MSU1文件头");
                        return false;
                    }

                    uint loopStart = (uint)(msuData[4] | (msuData[5] << 8) | (msuData[6] << 16) | (msuData[7] << 24));

                    int audioDataOffset = 8;
                    int audioDataLength = msuData.Length - audioDataOffset;
                    int sampleCount = audioDataLength / 2;

                    using (var writer = new BinaryWriter(memoryStream))
                    {
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                        writer.Write(0);
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                        writer.Write(16);
                        writer.Write((short)1);
                        writer.Write((short)2);
                        writer.Write(44100);
                        writer.Write(44100 * 2 * 2);
                        writer.Write((short)4);
                        writer.Write((short)16);
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                        writer.Write(sampleCount * 2);

                        for (int i = audioDataOffset; i < msuData.Length; i += 2)
                        {
                            short sample = (short)(msuData[i] | (msuData[i + 1] << 8));
                            writer.Write(sample);
                        }

                        writer.Seek(4, SeekOrigin.Begin);
                        writer.Write((int)(memoryStream.Length - 8));
                    }

                    File.WriteAllBytes(wavFilePath, memoryStream.ToArray());
                }
                return true;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }
    }
}