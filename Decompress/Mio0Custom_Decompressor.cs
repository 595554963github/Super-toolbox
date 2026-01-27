namespace super_toolbox
{
    public class Mio0Custom_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        private enum Endianness { Little, Big }
        private Endianness _endianness = Endianness.Big;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            string decompressedDir = Path.Combine(directoryPath, "Decompressed");
            Directory.CreateDirectory(decompressedDir);

            DecompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.mio0", SearchOption.AllDirectories);

                    if (allFiles.Length == 0)
                    {
                        var filesWithMio0 = new List<string>();
                        foreach (var filePath in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
                        {
                            if (ContainsMio0Data(filePath))
                            {
                                filesWithMio0.Add(filePath);
                            }
                        }
                        allFiles = filesWithMio0.ToArray();
                    }

                    if (allFiles.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的MIO0压缩文件");
                        OnDecompressionFailed("未找到有效的MIO0压缩文件");
                        return;
                    }

                    TotalFilesToDecompress = allFiles.Length;
                    int processedFiles = 0;

                    foreach (var filePath in allFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedFiles++;

                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        try
                        {
                            var results = DecompressAllMio0InFile(filePath, decompressedDir);
                            foreach (var result in results)
                            {
                                if (result.success)
                                {
                                    DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(result.outputPath)}");
                                    OnFileDecompressed(result.outputPath);
                                }
                                else
                                {
                                    DecompressionError?.Invoke(this, $"解压失败:{result.error}");
                                    OnDecompressionFailed($"解压失败:{result.error}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DecompressionError?.Invoke(this, $"解压文件{filePath}时出错:{ex.Message}");
                            OnDecompressionFailed($"解压文件{filePath}时出错:{ex.Message}");
                        }
                    }

                    OnDecompressionCompleted();
                    DecompressionProgress?.Invoke(this, $"解压完成,共处理{TotalFilesToDecompress}个文件");
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                DecompressionError?.Invoke(this, "解压操作已取消");
                OnDecompressionFailed("解压操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"解压过程出错:{ex.Message}");
                OnDecompressionFailed($"解压过程出错:{ex.Message}");
            }
        }

        private bool ContainsMio0Data(string filePath)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                return FindMio0Indices(fileData).Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private List<int> FindMio0Indices(byte[] data)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i <= data.Length - 4; i++)
            {
                if (data[i] == 'M' && data[i + 1] == 'I' && data[i + 2] == 'O' && data[i + 3] == '0')
                {
                    indices.Add(i);
                }
            }
            return indices;
        }

        private List<(bool success, string outputPath, string error)> DecompressAllMio0InFile(string filePath, string outputDir)
        {
            var results = new List<(bool, string, string)>();
            byte[] fileData = File.ReadAllBytes(filePath);

            var mio0Indices = FindMio0Indices(fileData);

            if (mio0Indices.Count == 0 && IsMio0File(filePath))
            {
                mio0Indices.Add(0);
            }

            for (int i = 0; i < mio0Indices.Count; i++)
            {
                int offset = mio0Indices[i];
                try
                {
                    byte[] decompressedData = DecompressMio0(fileData, offset);

                    string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                    string outputFileName = mio0Indices.Count > 1 ? $"{baseFileName}_{i}" : baseFileName;
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    File.WriteAllBytes(outputPath, decompressedData);

                    results.Add((true, outputPath, ""));
                }
                catch (Exception ex)
                {
                    results.Add((false, "", $"偏移量0x{offset:X8}处解压失败:{ex.Message}"));
                }
            }

            return results;
        }

        private bool IsMio0File(string filePath)
        {
            try
            {
                using var file = File.OpenRead(filePath);
                if (file.Length < 4) return false;
                byte[] header = new byte[4];
                file.Read(header, 0, 4);
                return System.Text.Encoding.ASCII.GetString(header) == "MIO0";
            }
            catch
            {
                return false;
            }
        }

        private byte[] DecompressMio0(byte[] data, int offset)
        {
            if (offset + 0x10 > data.Length)
                throw new ArgumentException("数据长度不足");

            string header = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            if (header != "MIO0")
                throw new ArgumentException("无效的MIO0头");

            int decompressedLength = ReadInt32(data, offset + 4);
            int compressedOffset = ReadInt32(data, offset + 8) + offset;
            int uncompressedOffset = ReadInt32(data, offset + 12) + offset;

            byte[] output = new byte[decompressedLength];
            int outputIndex = 0;
            int layoutByteIndex = offset + 0x10;
            int currentMask = 0;
            int bitsRemaining = 0;

            while (outputIndex < decompressedLength)
            {
                if (bitsRemaining == 0)
                {
                    if (layoutByteIndex >= data.Length)
                        break;
                    currentMask = data[layoutByteIndex++];
                    bitsRemaining = 8;
                }

                if ((currentMask & 0x80) != 0)
                {
                    if (uncompressedOffset >= data.Length)
                        break;
                    output[outputIndex++] = data[uncompressedOffset++];
                }
                else
                {
                    if (compressedOffset + 1 >= data.Length)
                        break;

                    byte byte1 = data[compressedOffset++];
                    byte byte2 = data[compressedOffset++];

                    int length = ((byte1 >> 4) & 0x0F) + 3;
                    int lookback = (((byte1 & 0x0F) << 8) | byte2) + 1;

                    if (outputIndex - lookback < 0)
                        throw new Exception("回看索引超出缓冲区");

                    for (int i = 0; i < length && outputIndex < decompressedLength; i++)
                    {
                        output[outputIndex] = output[outputIndex - lookback];
                        outputIndex++;
                    }
                }

                currentMask <<= 1;
                bitsRemaining--;
            }

            if (outputIndex != decompressedLength)
                throw new Exception($"解压大小不匹配:预期{decompressedLength},实际{outputIndex}");

            return output;
        }

        private int ReadInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length)
                throw new IndexOutOfRangeException("读取32位整数时索引越界");

            if (_endianness == Endianness.Big)
                return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            else
                return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
