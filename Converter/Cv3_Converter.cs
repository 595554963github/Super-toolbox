using System.Buffers;

namespace super_toolbox
{
    public class Cv3_Converter : BaseExtractor
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

            var cv3Files = Directory.GetFiles(directoryPath, "*.cv3", SearchOption.AllDirectories).ToArray();
            TotalFilesToConvert = cv3Files.Length;
            int successCount = 0;

            try
            {
                foreach (var cv3FilePath in cv3Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(cv3FilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}");

                    try
                    {
                        string outputFile = await ConvertCv3ToWav(cv3FilePath, cancellationToken);

                        if (!string.IsNullOrEmpty(outputFile) && File.Exists(outputFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(outputFile)}");
                            OnFileConverted(outputFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}转换失败");
                            OnConversionFailed($"{fileName}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}处理错误:{ex.Message}");
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

        private async Task<string> ConvertCv3ToWav(string cv3FilePath, CancellationToken cancellationToken)
        {
            await Task.Yield();

            try
            {
                using (FileStream inputStream = new FileStream(cv3FilePath, FileMode.Open, FileAccess.Read))
                {
                    if (inputStream.Length <= 0)
                    {
                        throw new IOException("CV3文件为空");
                    }

                    string outputFileName = Path.ChangeExtension(cv3FilePath, ".wav");

                    using (FileStream outputStream = new FileStream(outputFileName, FileMode.Create, FileAccess.Write))
                    {
                        int size = (int)inputStream.Length;

                        if (size == int.MinValue)
                        {
                            throw new ArgumentException($"输入流太大 (长度为{inputStream.Length}字节, 最大支持{int.MaxValue})");
                        }

                        byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
                        try
                        {
                            Memory<byte> data = buffer.AsMemory(0, size);
                            inputStream.Seek(0, SeekOrigin.Begin);
                            await inputStream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);

                            // 提取WAV数据
                            Memory<byte> waveData = WriteWave(data.Span[..WAVEFORMATEX_SIZE],
                                data.Span.Slice(22, ReadInt32(data.Span, 18)),
                                checkIfMagicExists: true,
                                out bool shouldUseInputData);

                            await outputStream.WriteAsync(shouldUseInputData ? data : waveData, cancellationToken).ConfigureAwait(false);
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
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return string.Empty;
            }
        }

        private const int WAVEFORMATEX_SIZE = (sizeof(uint) * 2) + (sizeof(ushort) * 4);

        private Memory<byte> WriteWave(ReadOnlySpan<byte> formatData, ReadOnlySpan<byte> waveData, bool checkIfMagicExists, out bool shouldUseInputData)
        {
            shouldUseInputData = false;

            if (checkIfMagicExists && waveData.Length >= 4)
            {
                // 检查是否已经是WAV格式
                if (waveData[0] == 'R' && waveData[1] == 'I' && waveData[2] == 'F' && waveData[3] == 'F')
                {
                    shouldUseInputData = true;
                    return Memory<byte>.Empty;
                }
            }

            using MemoryStream outputStream = new MemoryStream(44 + waveData.Length);

            // RIFF头
            outputStream.Write("RIFF"u8);

            // 文件大小 (数据大小 + 36)
            uint fileSize = (uint)(waveData.Length + 36);
            outputStream.Write(BitConverter.GetBytes(fileSize));

            // WAVE标识
            outputStream.Write("WAVE"u8);

            // fmt块
            outputStream.Write("fmt "u8);

            // fmt块大小 (16)
            uint fmtSize = 16;
            outputStream.Write(BitConverter.GetBytes(fmtSize));

            // 写入音频格式信息
            outputStream.Write(formatData);

            // data块
            outputStream.Write("data"u8);

            // data块大小
            outputStream.Write(BitConverter.GetBytes((uint)waveData.Length));

            // 音频数据
            outputStream.Write(waveData);

            return outputStream.GetBuffer().AsMemory(0, (int)outputStream.Length);
        }

        private int ReadInt32(ReadOnlySpan<byte> data, int offset)
        {
            return BitConverter.ToInt32(data.Slice(offset, sizeof(int)));
        }
    }
}