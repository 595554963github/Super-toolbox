using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class W64ToWav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var w64Files = Directory.GetFiles(directoryPath, "*.w64", SearchOption.AllDirectories)
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

            TotalFilesToConvert = w64Files.Length;
            int successCount = 0;

            try
            {
                foreach (var w64FilePath in w64Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(w64FilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.w64");

                    string fileDirectory = Path.GetDirectoryName(w64FilePath) ?? string.Empty;
                    string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavFile))
                        {
                            File.Delete(wavFile);
                        }

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertW64ToWav(w64FilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.w64转换失败");
                            OnConversionFailed($"{fileName}.w64转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.w64处理错误:{ex.Message}");
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

        private bool ConvertW64ToWav(string w64FilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                using (FileStream input = new FileStream(w64FilePath, FileMode.Open, FileAccess.Read))
                using (FileStream output = new FileStream(wavFilePath, FileMode.Create, FileAccess.Write))
                using (BinaryReader reader = new BinaryReader(input))
                using (BinaryWriter writer = new BinaryWriter(output))
                {
                    writer.Write(Encoding.ASCII.GetBytes("RIFF"));

                    reader.BaseStream.Seek(0x10, SeekOrigin.Begin);
                    long w64Value = reader.ReadInt64();
                    int wavRiffSize = (int)(w64Value - 68);
                    writer.Write(wavRiffSize);

                    writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                    reader.BaseStream.Seek(0x28, SeekOrigin.Begin);
                    writer.Write(Encoding.ASCII.GetBytes("fmt "));

                    writer.Write(16);

                    reader.BaseStream.Seek(0x40, SeekOrigin.Begin);
                    byte[] fmtData = reader.ReadBytes(16);
                    writer.Write(fmtData);

                    writer.Write(Encoding.ASCII.GetBytes("data"));

                    int dataSize = wavRiffSize - 36;
                    writer.Write(dataSize);

                    reader.BaseStream.Seek(0x68, SeekOrigin.Begin);
                    byte[] buffer = new byte[65536];
                    int remaining = dataSize;
                    int bytesRead;
                    while (remaining > 0)
                    {
                        int toRead = Math.Min(buffer.Length, remaining);
                        bytesRead = reader.Read(buffer, 0, toRead);
                        if (bytesRead == 0) break;
                        writer.Write(buffer, 0, bytesRead);
                        remaining -= bytesRead;
                    }

                    return true;
                }
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