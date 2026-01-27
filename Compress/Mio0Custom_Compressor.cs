namespace super_toolbox
{
    public class Mio0Custom_Compressor : BaseExtractor
    {
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            if (filesToCompress.Length == 0)
            {
                CompressionError?.Invoke(this, "未找到需要压缩的文件");
                OnCompressionFailed("未找到需要压缩的文件");
                return;
            }

            string compressedDir = Path.Combine(directoryPath, "Compressed");
            Directory.CreateDirectory(compressedDir);

            CompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(compressedDir, "*.mio0", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToCompress = filesToCompress.Length;
                    int processedFiles = 0;

                    foreach (var filePath in filesToCompress)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedFiles++;

                        CompressionProgress?.Invoke(this, $"正在压缩文件({processedFiles}/{TotalFilesToCompress}): {Path.GetFileName(filePath)}");

                        string relativePath = GetRelativePath(directoryPath, filePath);
                        string outputPath = Path.Combine(compressedDir, relativePath + ".mio0");
                        string outputDir = Path.GetDirectoryName(outputPath) ??
                            throw new InvalidOperationException($"无法确定输出目录路径:{outputPath}");

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        try
                        {
                            byte[] fileData = File.ReadAllBytes(filePath);
                            byte[] compressedData = CompressWithMIO0(fileData);

                            File.WriteAllBytes(outputPath, compressedData);

                            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                            {
                                CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)}");
                                OnFileCompressed(outputPath);
                            }
                            else
                            {
                                CompressionError?.Invoke(this, $"压缩成功但输出文件异常:{outputPath}");
                                OnCompressionFailed($"压缩成功但输出文件异常:{outputPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            CompressionError?.Invoke(this, $"压缩文件{filePath}时出错:{ex.Message}");
                            OnCompressionFailed($"压缩文件{filePath}时出错:{ex.Message}");
                        }
                    }

                    OnCompressionCompleted();
                    CompressionProgress?.Invoke(this, $"压缩完成,共压缩{TotalFilesToCompress}个文件");
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CompressionError?.Invoke(this, "压缩操作已取消");
                OnCompressionFailed("压缩操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"压缩过程出错:{ex.Message}");
                OnCompressionFailed($"压缩过程出错:{ex.Message}");
            }
        }

        private byte[] CompressWithMIO0(byte[] inputData)
        {
            List<byte> layoutBits = new List<byte>();
            List<byte> uncompressedData = new List<byte>();
            List<(int offset, int length)> compressedData = new List<(int, int)>();
            byte[] slidingWindow = new byte[4096];
            int windowStart = 0;
            int windowSize = 0;
            int totalProcessed = 0;

            int i = 0;
            while (i < inputData.Length)
            {
                int bestOffset = 0;
                int bestLength = 0;

                if (windowSize > 0)
                {
                    int searchStart = Math.Max(0, windowStart);
                    int searchEnd = windowStart + windowSize;

                    for (int j = searchStart; j < searchEnd; j++)
                    {
                        int windowPos = j % 4096;
                        if (slidingWindow[windowPos] == inputData[i])
                        {
                            int matchLength = 1;
                            while (matchLength < 18 &&
                                   i + matchLength < inputData.Length &&
                                   (j + matchLength) < searchEnd &&
                                   slidingWindow[(j + matchLength) % 4096] == inputData[i + matchLength])
                            {
                                matchLength++;
                            }

                            if (matchLength >= 3 && matchLength > bestLength)
                            {
                                bestLength = matchLength;
                                bestOffset = (searchEnd - j);
                            }
                        }
                    }
                }

                if (bestLength >= 3)
                {
                    layoutBits.Add(0);
                    compressedData.Add((bestOffset, bestLength));

                    for (int j = 0; j < bestLength; j++)
                    {
                        slidingWindow[(windowStart + windowSize) % 4096] = inputData[i + j];
                        if (windowSize < 4096) windowSize++;
                        else windowStart = (windowStart + 1) % 4096;
                    }

                    i += bestLength;
                    totalProcessed += bestLength;
                }
                else
                {
                    layoutBits.Add(1);
                    uncompressedData.Add(inputData[i]);
                    slidingWindow[(windowStart + windowSize) % 4096] = inputData[i];
                    if (windowSize < 4096) windowSize++;
                    else windowStart = (windowStart + 1) % 4096;

                    i++;
                    totalProcessed++;
                }

                if (windowStart + windowSize > 4096)
                {
                    int overflow = (windowStart + windowSize) - 4096;
                    windowStart = overflow;
                    windowSize = 4096;
                }
            }

            return BuildMIO0CompressedBlock(layoutBits, uncompressedData, compressedData, inputData.Length);
        }

        private byte[] BuildMIO0CompressedBlock(List<byte> layoutBits, List<byte> uncompressedData, List<(int offset, int length)> compressedData, int decompressedSize)
        {
            List<byte> output = new List<byte>();

            output.AddRange(System.Text.Encoding.ASCII.GetBytes("MIO0"));

            byte[] sizeBytes = BitConverter.GetBytes(decompressedSize);
            Array.Reverse(sizeBytes);
            output.AddRange(sizeBytes);

            List<byte> layoutBytes = new List<byte>();
            for (int i = 0; i < layoutBits.Count; i += 8)
            {
                byte layoutByte = 0;
                for (int j = 0; j < 8 && i + j < layoutBits.Count; j++)
                {
                    if (layoutBits[i + j] == 1)
                    {
                        layoutByte |= (byte)(1 << (7 - j));
                    }
                }
                layoutBytes.Add(layoutByte);
            }

            while (layoutBytes.Count % 4 != 0)
            {
                layoutBytes.Add(0);
            }

            List<byte> compressedBytes = new List<byte>();
            foreach (var (offset, length) in compressedData)
            {
                int encodedOffset = offset - 1;
                int encodedLength = length - 3;

                byte byte1 = (byte)((encodedLength << 4) | ((encodedOffset >> 8) & 0x0F));
                byte byte2 = (byte)(encodedOffset & 0xFF);

                compressedBytes.Add(byte1);
                compressedBytes.Add(byte2);
            }

            int compressedOffset = 16 + layoutBytes.Count;
            int uncompressedOffset = compressedOffset + compressedBytes.Count;

            byte[] compOffsetBytes = BitConverter.GetBytes(compressedOffset);
            Array.Reverse(compOffsetBytes);
            output.AddRange(compOffsetBytes);

            byte[] uncompOffsetBytes = BitConverter.GetBytes(uncompressedOffset);
            Array.Reverse(uncompOffsetBytes);
            output.AddRange(uncompOffsetBytes);

            output.AddRange(layoutBytes);
            output.AddRange(compressedBytes);
            output.AddRange(uncompressedData);

            return output.ToArray();
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
