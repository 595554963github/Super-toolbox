using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2msu_Converter : BaseExtractor
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

            var wavFiles = Directory.GetFiles(directoryPath, "*.wav", SearchOption.AllDirectories)
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

                    try
                    {
                        string msuFile = Path.Combine(fileDirectory, $"{fileName}.raw");

                        if (File.Exists(msuFile))
                            File.Delete(msuFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToMsu(wavFilePath, msuFile, 0, string.Empty, cancellationToken), cancellationToken);

                        if (conversionSuccess && File.Exists(msuFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(msuFile)}");
                            OnFileConverted(msuFile);
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

        private bool ConvertWavToMsu(string wavFilePath, string msuFilePath, int loopPoint, string introFilePath, CancellationToken cancellationToken)
        {
            try
            {
                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    using (var reader = new BinaryReader(wavStream))
                    {
                        if (reader.ReadInt32() != 0x46464952)
                        {
                            ConversionError?.Invoke(this, "无效的RIFF头");
                            return false;
                        }

                        wavStream.Seek(20, SeekOrigin.Begin);

                        if (reader.ReadInt16() != 1)
                        {
                            ConversionError?.Invoke(this, "不是PCM格式");
                            return false;
                        }

                        short channels = reader.ReadInt16();
                        int sampleRate = reader.ReadInt32();
                        wavStream.Seek(34, SeekOrigin.Begin);
                        short bitsPerSample = reader.ReadInt16();

                        if (channels != 2 || sampleRate != 44100 || bitsPerSample != 16)
                        {
                            ConversionError?.Invoke(this, $"不支持的格式: {bitsPerSample}bit, {sampleRate}Hz, {channels}ch");
                            return false;
                        }

                        if (reader.ReadInt32() != 0x61746164)
                        {
                            ConversionError?.Invoke(this, "找不到data块");
                            return false;
                        }

                        int dataSize = reader.ReadInt32();

                        using (var msuStream = File.Create(msuFilePath))
                        {
                            using (var writer = new BinaryWriter(msuStream))
                            {
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("MSU1"));
                                writer.Write(loopPoint);

                                if (!string.IsNullOrEmpty(introFilePath) && File.Exists(introFilePath))
                                {
                                    using (var introStream = File.OpenRead(introFilePath))
                                    {
                                        introStream.CopyTo(msuStream);
                                    }
                                }

                                wavStream.CopyTo(msuStream);
                            }
                        }
                    }
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