using System.Text;

namespace super_toolbox
{
    public enum Endianness
    {
        Little,
        Big
    }

    public class Mio0_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        private const int HEADER_SIZE = 0x10;
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
                    DecompressionProgress?.Invoke(this, $"解压完成，共处理{TotalFilesToDecompress}个文件");
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

            int startIndex = Math.Min(0x0D0000, data.Length);

            for (int i = startIndex; i < data.Length - 4; i += 16)
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
                    byte[] decompressedData = DecompressMio0(fileData, offset, _endianness);

                    string outputFileName = GetOriginalFileName(filePath, mio0Indices.Count, i);
                    string outputPath = Path.Combine(outputDir, outputFileName);

                    File.WriteAllBytes(outputPath, decompressedData);

                    results.Add((true, outputPath, ""));
                }
                catch (Exception ex)
                {
                    results.Add((false, "", $"偏移量0x{offset:X8}处解压失败: {ex.Message}"));
                }
            }

            return results;
        }

        private string GetOriginalFileName(string compressedFilePath, int totalMio0Blocks, int currentIndex)
        {
            string fileName = Path.GetFileName(compressedFilePath);

            if (fileName.EndsWith(".mio0", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - 5);
            }
            return fileName;
        }

        private bool IsMio0File(string filePath)
        {
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    if (file.Length < 4) return false;
                    byte[] header = new byte[4];
                    file.Read(header, 0, 4);
                    return Encoding.ASCII.GetString(header) == "MIO0";
                }
            }
            catch
            {
                return false;
            }
        }

        private byte[] DecompressMio0(byte[] data, int offset, Endianness endianness)
        {
            if (offset + HEADER_SIZE > data.Length)
            {
                throw new ArgumentException("数据长度不足，无法读取MIO0头");
            }

            string header = Encoding.ASCII.GetString(data, offset, 4);
            if (header != "MIO0")
            {
                throw new ArgumentException("无效的MIO0头");
            }

            int decompressedLength = ReadInt32(data, offset + 4, endianness);

            int compressedOffset = ReadInt32(data, offset + 8, endianness) + offset;

            int uncompressedOffset = ReadInt32(data, offset + 12, endianness) + offset;

            byte[] output = new byte[decompressedLength];
            int outputIndex = 0;
            int layoutBitIndex = 0;

            int compressedIndex = 0;
            int uncompressedIndex = 0;

            while (outputIndex < decompressedLength)
            {
                bool isUncompressed = ReadLayoutBit(data, offset, layoutBitIndex);
                layoutBitIndex++;

                if (outputIndex >= decompressedLength)
                    break;

                if (isUncompressed)
                {
                    if (uncompressedOffset + uncompressedIndex >= data.Length)
                        throw new IndexOutOfRangeException("未压缩数据索引越界");

                    output[outputIndex] = data[uncompressedOffset + uncompressedIndex];
                    uncompressedIndex++;
                    outputIndex++;
                }
                else
                {
                    if (compressedOffset + compressedIndex + 2 > data.Length)
                        throw new IndexOutOfRangeException("压缩数据索引越界");

                    byte byte1 = data[compressedOffset + compressedIndex];
                    byte byte2 = data[compressedOffset + compressedIndex + 1];
                    compressedIndex += 2;

                    int length = ((byte1 & 0xF0) >> 4) + 3;
                    int lookbackIndex = ((byte1 & 0x0F) << 8) | byte2;
                    lookbackIndex += 1;

                    if (length < 3 || length > 18)
                        throw new Exception($"不合理的长度值: {length}");

                    if (lookbackIndex < 1 || lookbackIndex > 4096)
                        throw new Exception($"不合理的索引值: {lookbackIndex}");

                    if (outputIndex - lookbackIndex < 0)
                        throw new Exception("回看索引超出当前输出缓冲区");

                    for (int i = 0; i < length && outputIndex < decompressedLength; i++)
                    {
                        output[outputIndex] = output[outputIndex - lookbackIndex];
                        outputIndex++;
                    }
                }
            }

            return output;
        }

        private bool ReadLayoutBit(byte[] data, int baseOffset, int bitIndex)
        {
            int byteIndex = baseOffset + HEADER_SIZE + (bitIndex / 8);
            int bitOffset = 7 - (bitIndex % 8);

            if (byteIndex >= data.Length)
                throw new IndexOutOfRangeException("布局位索引越界");

            return (data[byteIndex] & (1 << bitOffset)) != 0;
        }

        private int ReadInt32(byte[] data, int offset, Endianness endianness)
        {
            if (offset + 4 > data.Length)
                throw new IndexOutOfRangeException("读取32位整数时索引越界");

            if (endianness == Endianness.Big)
            {
                return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            }
            else
            {
                return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}