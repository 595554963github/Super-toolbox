namespace super_toolbox
{
    public class Mio0_Compressor : BaseExtractor
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

            CompressionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

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
                    CompressionProgress?.Invoke(this, $"压缩完成，共压缩{TotalFilesToCompress}个文件");
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
            List<byte> dictionary = new List<byte>();
            List<byte> uncompressedData = new List<byte>();
            List<int[]> compressedData = new List<int[]>();

            int maxDictionarySize = 4096;
            int maxMatchLength = 18;
            int minimumMatchSize = 2;
            int decompressedSize = 0;

            for (int i = 0; i < inputData.Length; i++)
            {
                if (dictionary.Contains(inputData[i]))
                {
                    int[] matches = FindAllMatches(ref dictionary, inputData[i]);
                    int[] bestMatch = FindLargestMatch(ref dictionary, matches, ref inputData, i, maxMatchLength);

                    if (bestMatch[1] > minimumMatchSize)
                    {
                        layoutBits.Add(0);
                        bestMatch[0] = dictionary.Count - bestMatch[0];

                        for (int j = 0; j < bestMatch[1]; j++)
                        {
                            dictionary.Add(inputData[i + j]);
                        }

                        i = i + bestMatch[1] - 1;
                        compressedData.Add(bestMatch);
                        decompressedSize += bestMatch[1];
                    }
                    else
                    {
                        layoutBits.Add(1);
                        uncompressedData.Add(inputData[i]);
                        dictionary.Add(inputData[i]);
                        decompressedSize++;
                    }
                }
                else
                {
                    layoutBits.Add(1);
                    uncompressedData.Add(inputData[i]);
                    dictionary.Add(inputData[i]);
                    decompressedSize++;
                }

                if (dictionary.Count > maxDictionarySize)
                {
                    int overflow = dictionary.Count - maxDictionarySize;
                    dictionary.RemoveRange(0, overflow);
                }
            }

            return BuildMIO0CompressedBlock(ref layoutBits, ref uncompressedData, ref compressedData, decompressedSize);
        }

        private int[] FindAllMatches(ref List<byte> dictionary, byte targetByte)
        {
            List<int> matches = new List<int>();
            for (int i = 0; i < dictionary.Count; i++)
            {
                if (dictionary[i] == targetByte)
                {
                    matches.Add(i);
                }
            }
            return matches.ToArray();
        }

        private int[] FindLargestMatch(ref List<byte> dictionary, int[] matches, ref byte[] file, int fileIndex, int maxMatchLength)
        {
            int bestMatchIndex = -1;
            int bestMatchLength = 0;

            foreach (int matchIndex in matches)
            {
                int matchLength = 1;
                while (matchLength < maxMatchLength &&
                       matchIndex + matchLength < dictionary.Count &&
                       fileIndex + matchLength < file.Length &&
                       dictionary[matchIndex + matchLength] == file[fileIndex + matchLength])
                {
                    matchLength++;
                }

                if (matchLength > bestMatchLength)
                {
                    bestMatchLength = matchLength;
                    bestMatchIndex = matchIndex;
                }
            }

            return new int[] { bestMatchIndex, bestMatchLength };
        }

        private byte[] BuildMIO0CompressedBlock(ref List<byte> layoutBits, ref List<byte> uncompressedData, ref List<int[]> offsetLengthPairs, int decompressedSize)
        {
            List<byte> finalMIO0Block = new List<byte>();
            List<byte> layoutBytes = new List<byte>();
            List<byte> compressedDataBytes = new List<byte>();

            finalMIO0Block.AddRange(System.Text.Encoding.ASCII.GetBytes("MIO0"));

            byte[] decompressedSizeArray = BitConverter.GetBytes(decompressedSize);
            Array.Reverse(decompressedSizeArray);
            finalMIO0Block.AddRange(decompressedSizeArray);

            while (layoutBits.Count > 0)
            {
                while (layoutBits.Count < 8)
                {
                    layoutBits.Add(0);
                }

                string layoutBitsString = string.Empty;
                for (int i = 0; i < 8; i++)
                {
                    layoutBitsString += layoutBits[i].ToString();
                }

                byte layoutByte = Convert.ToByte(layoutBitsString, 2);
                layoutBytes.Add(layoutByte);
                layoutBits.RemoveRange(0, 8);
            }

            foreach (int[] offsetLengthPair in offsetLengthPairs)
            {
                int offset = offsetLengthPair[0] - 1;
                int length = offsetLengthPair[1] - 3;

                int compressedInt = (length << 12) | offset;
                byte[] compressed2Byte = new byte[2];
                compressed2Byte[0] = (byte)(compressedInt & 0xFF);
                compressed2Byte[1] = (byte)((compressedInt >> 8) & 0xFF);

                compressedDataBytes.Add(compressed2Byte[1]);
                compressedDataBytes.Add(compressed2Byte[0]);
            }

            while (layoutBytes.Count % 4 != 0)
            {
                layoutBytes.Add(0);
            }

            int compressedOffset = 16 + layoutBytes.Count;
            int uncompressedOffset = compressedOffset + compressedDataBytes.Count;

            byte[] compressedOffsetArray = BitConverter.GetBytes(compressedOffset);
            Array.Reverse(compressedOffsetArray);
            finalMIO0Block.AddRange(compressedOffsetArray);

            byte[] uncompressedOffsetArray = BitConverter.GetBytes(uncompressedOffset);
            Array.Reverse(uncompressedOffsetArray);
            finalMIO0Block.AddRange(uncompressedOffsetArray);

            finalMIO0Block.AddRange(layoutBytes);

            finalMIO0Block.AddRange(compressedDataBytes);

            finalMIO0Block.AddRange(uncompressedData);

            return finalMIO0Block.ToArray();
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