using System.Buffers;

namespace super_toolbox
{
    public class Cv01_Converter : BaseExtractor
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

            var cv0Files = Directory.GetFiles(directoryPath, "*.cv0", SearchOption.AllDirectories)
                                   .Concat(Directory.GetFiles(directoryPath, "*.cv1", SearchOption.AllDirectories))
                                   .ToArray();

            TotalFilesToConvert = cv0Files.Length;
            int successCount = 0;

            try
            {
                foreach (var cv0FilePath in cv0Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(cv0FilePath);
                    string extension = Path.GetExtension(cv0FilePath).ToLower();
                    string operation = extension == ".cv0" ? "文本解密" : "CSV解密";

                    ConversionProgress?.Invoke(this, $"正在{operation}:{fileName}");

                    try
                    {
                        string outputFile = await DecryptCv01ToTxt(cv0FilePath, cancellationToken);

                        if (!string.IsNullOrEmpty(outputFile) && File.Exists(outputFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"{operation}成功:{Path.GetFileName(outputFile)}");
                            OnFileConverted(outputFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}{operation}失败");
                            OnConversionFailed($"{fileName}{operation}失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"{operation}异常:{ex.Message}");
                        OnConversionFailed($"{fileName}处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功解密{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功解密任何文件");
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

        private async Task<string> DecryptCv01ToTxt(string cv01FilePath, CancellationToken cancellationToken)
        {
            await Task.Yield();

            try
            {
                using (FileStream inputStream = new FileStream(cv01FilePath, FileMode.Open, FileAccess.Read))
                {
                    if (inputStream.Length <= 0)
                    {
                        throw new IOException("文件为空");
                    }

                    string extension = Path.GetExtension(cv01FilePath).ToLower();
                    string outputFileName = extension == ".cv0"
                        ? Path.ChangeExtension(cv01FilePath, ".txt")
                        : Path.ChangeExtension(cv01FilePath, ".csv");

                    using (FileStream outputStream = new FileStream(outputFileName, FileMode.Create, FileAccess.Write))
                    {
                        int size = (int)inputStream.Length;

                        if (size == int.MinValue)
                        {
                            throw new ArgumentException($"输入流太大(长度为{inputStream.Length}字节,最大支持{int.MaxValue})");
                        }

                        byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
                        try
                        {
                            Memory<byte> data = buffer.AsMemory(0, size);
                            inputStream.Seek(0, SeekOrigin.Begin);
                            await inputStream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);

                            Crypt(data.Span, 0x8B, 0x71, 0x95);

                            await outputStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    return outputFileName;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解密过程异常:{ex.Message}");
                return string.Empty;
            }
        }
        private void Crypt(Span<byte> data, byte key1, byte key2, byte key3)
        {
            for (int c = 0; c < data.Length; c++)
            {
                data[c] ^= key1;
                key1 += key2;
                key2 += key3;
            }
        }
    }
}