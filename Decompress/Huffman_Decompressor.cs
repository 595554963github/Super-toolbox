namespace super_toolbox
{
    public class Huffman_Decompressor : BaseExtractor
    {
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
                    var allFiles = Directory.GetFiles(directoryPath, "*.huff");
                    if (allFiles.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到Huffman压缩文件");
                        OnDecompressionFailed("未找到Huffman压缩文件");
                        return;
                    }

                    TotalFilesToDecompress = allFiles.Length;
                    int processedFiles = 0;

                    foreach (var filePath in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;
                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(decompressedDir, fileName);

                        try
                        {
                            byte[] compressedData = File.ReadAllBytes(filePath);
                            byte[] decompressedData = HuffmanDecompress(compressedData);
                            File.WriteAllBytes(outputPath, decompressedData);

                            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                            {
                                DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)}");
                                OnFileDecompressed(outputPath);
                            }
                            else
                            {
                                DecompressionError?.Invoke(this, $"解压成功但输出文件异常:{outputPath}");
                                OnDecompressionFailed($"解压成功但输出文件异常:{outputPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DecompressionError?.Invoke(this, $"解压文件{filePath}时出错:{ex.Message}");
                            OnDecompressionFailed($"解压文件{filePath}时出错:{ex.Message}");
                        }
                    }

                    OnDecompressionCompleted();
                    DecompressionProgress?.Invoke(this, $"解压完成,共解压{TotalFilesToDecompress}个文件");
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

        private byte[] HuffmanDecompress(byte[] compressedData)
        {
            using var input = new MemoryStream(compressedData);
            using var reader = new BinaryReader(input);

            DecompressorHuffmanNode root = ReadTree(reader);
            int originalLength = reader.ReadInt32();

            using var bitStream = new DecompressorBitStream(input, DecompressorBitStreamEndianness.Msb, DecompressorBitStreamMode.Read);
            var result = new List<byte>();

            for (int i = 0; i < originalLength; i++)
            {
                DecompressorHuffmanNode node = root;
                while (node.Left != null && node.Right != null)
                {
                    int bit = bitStream.ReadBit();
                    node = bit == 0 ? node.Left : node.Right;
                }
                result.Add(node.Value);
            }

            return result.ToArray();
        }

        private DecompressorHuffmanNode ReadTree(BinaryReader reader)
        {
            byte flag = reader.ReadByte();
            if (flag == 0)
            {
                byte value = reader.ReadByte();
                return new DecompressorHuffmanNode(value, 0);
            }
            else
            {
                DecompressorHuffmanNode left = ReadTree(reader);
                DecompressorHuffmanNode right = ReadTree(reader);
                return new DecompressorHuffmanNode(left, right);
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }

    public class DecompressorHuffmanNode
    {
        public byte Value { get; }
        public int Freq { get; }
        public DecompressorHuffmanNode Left { get; }
        public DecompressorHuffmanNode Right { get; }

        public DecompressorHuffmanNode(byte value, int freq)
        {
            Value = value;
            Freq = freq;
            Left = null!;
            Right = null!;
        }

        public DecompressorHuffmanNode(DecompressorHuffmanNode left, DecompressorHuffmanNode right)
        {
            Value = 0;
            Freq = left.Freq + right.Freq;
            Left = left;
            Right = right;
        }
    }

    public class DecompressorBitStream : IDisposable
    {
        private Stream stream;
        private DecompressorBitStreamEndianness endianness;
        private DecompressorBitStreamMode mode;
        private byte currentByte;
        private int bitPosition;

        public DecompressorBitStream(Stream stream, DecompressorBitStreamEndianness endianness, DecompressorBitStreamMode mode)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.endianness = endianness;
            this.mode = mode;
            currentByte = 0;
            bitPosition = mode == DecompressorBitStreamMode.Read ? 8 : 0;
        }

        public int ReadBit()
        {
            if (bitPosition == 8)
            {
                int b = stream.ReadByte();
                if (b == -1) return -1;
                currentByte = (byte)b;
                bitPosition = 0;
            }

            int bit;
            if (endianness == DecompressorBitStreamEndianness.Msb)
                bit = (currentByte >> (7 - bitPosition)) & 1;
            else
                bit = (currentByte >> bitPosition) & 1;

            bitPosition++;
            return bit;
        }

        public void Dispose()
        {
        }
    }

    public enum DecompressorBitStreamEndianness { Msb, Lsb }
    public enum DecompressorBitStreamMode { Read, Write }
}
