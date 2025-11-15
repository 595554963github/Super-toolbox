namespace super_toolbox
{
    public class Yay0_Decompressor : BaseExtractor
    {
        private static readonly byte[] Yay0Magic = { 0x59, 0x61, 0x79, 0x30 }; // "Yay0"
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
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
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsYay0File).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的Yay0压缩文件");
                        OnDecompressionFailed("未找到有效的Yay0压缩文件");
                        return;
                    }
                    TotalFilesToDecompress = filesToProcess.Length;
                    int processedFiles = 0;
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;
                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(decompressedDir, fileName);
                        try
                        {
                            byte[] compressedData = File.ReadAllBytes(filePath);
                            byte[]? decompressedData = DecompressYay0(compressedData);
                            if (decompressedData != null && decompressedData.Length > 0)
                            {
                                File.WriteAllBytes(outputPath, decompressedData);
                                DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)} (大小: {decompressedData.Length} 字节)");
                                OnFileDecompressed(outputPath);
                            }
                            else
                            {
                                DecompressionError?.Invoke(this, $"解压失败或输出为空: {filePath}");
                                OnDecompressionFailed($"解压失败或输出为空: {filePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DecompressionError?.Invoke(this, $"解压文件{filePath}时出错:{ex.Message}");
                            OnDecompressionFailed($"解压文件{filePath}时出错:{ex.Message}");
                        }
                    }
                    OnDecompressionCompleted();
                    DecompressionProgress?.Invoke(this, $"解压完成，共解压{TotalFilesToDecompress}个文件");
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
        private bool IsYay0File(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 16) return false;
                    byte[] header = new byte[4];
                    int bytesRead = fs.Read(header, 0, 4);
                    return bytesRead == 4 && header.SequenceEqual(Yay0Magic);
                }
            }
            catch
            {
                return false;
            }
        }
        private byte[]? DecompressYay0(byte[] data)
        {
            if (data == null || data.Length < 16)
                return Array.Empty<byte>();
            if (data[0] != 0x59 || data[1] != 0x61 || data[2] != 0x79 || data[3] != 0x30)
                return Array.Empty<byte>();
            uint decompressedSize = ReadUInt32BigEndian(data, 4);
            uint linkTableOffset = ReadUInt32BigEndian(data, 8);
            uint chunkOffset = ReadUInt32BigEndian(data, 12);
            if (linkTableOffset >= data.Length || chunkOffset >= data.Length)
                return Array.Empty<byte>();
            byte[] result = new byte[decompressedSize];
            int resultPos = 0;
            int maskBitCounter = 0;
            uint currentMask = 0;
            int otherIdx = 16;
            int linkTableIdx = (int)linkTableOffset;
            int chunkIdx = (int)chunkOffset;
            try
            {
                while (resultPos < decompressedSize)
                {
                    if (maskBitCounter == 0)
                    {
                        if (otherIdx + 4 > data.Length)
                            break;
                        currentMask = ReadUInt32BigEndian(data, otherIdx);
                        otherIdx += 4;
                        maskBitCounter = 32;
                    }
                    if ((currentMask & 0x80000000) != 0)
                    {
                        if (chunkIdx >= data.Length)
                            break;
                        result[resultPos] = data[chunkIdx];
                        resultPos++;
                        chunkIdx++;
                    }
                    else
                    {
                        if (linkTableIdx + 1 >= data.Length)
                            break;
                        ushort link = ReadUInt16BigEndian(data, linkTableIdx);
                        linkTableIdx += 2;
                        int offset = resultPos - (link & 0x0FFF) - 1;
                        int count = link >> 12;
                        if (count == 0)
                        {
                            if (chunkIdx >= data.Length)
                                break;
                            byte countModifier = data[chunkIdx];
                            chunkIdx++;
                            count = countModifier + 18;
                        }
                        else
                        {
                            count += 2;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            if (resultPos >= decompressedSize)
                                break;
                            if (offset + i >= 0 && offset + i < resultPos)
                            {
                                result[resultPos] = result[offset + i];
                            }
                            else
                            {
                                result[resultPos] = 0;
                            }
                            resultPos++;
                        }
                    }
                    currentMask <<= 1;
                    maskBitCounter--;
                }
                if (resultPos != decompressedSize)
                {
                    if (resultPos > 0)
                    {
                        Array.Resize(ref result, resultPos);
                        return result;
                    }
                    else
                    {
                        return Array.Empty<byte>();
                    }
                }
                return result;
            }
            catch (Exception)
            {
                return Array.Empty<byte>();
            }
        }
        private uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            if (data == null || offset + 3 >= data.Length)
                return 0;
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }
        private ushort ReadUInt16BigEndian(byte[] data, int offset)
        {
            if (data == null || offset + 1 >= data.Length)
                return 0;
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}