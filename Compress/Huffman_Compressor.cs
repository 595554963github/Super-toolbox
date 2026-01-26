namespace super_toolbox
{
    public class Huffman_Compressor : BaseExtractor
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

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*");
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
                    foreach (var file in Directory.GetFiles(compressedDir, "*.huff"))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToCompress = filesToCompress.Length;
                    int processedFiles = 0;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;
                        CompressionProgress?.Invoke(this, $"正在压缩文件({processedFiles}/{TotalFilesToCompress}): {Path.GetFileName(filePath)}");

                        string fileName = Path.GetFileName(filePath);
                        string outputPath = Path.Combine(compressedDir, fileName + ".huff");

                        try
                        {
                            byte[] fileBytes = File.ReadAllBytes(filePath);
                            byte[] compressedBytes = HuffmanCompress(fileBytes);
                            File.WriteAllBytes(outputPath, compressedBytes);

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

        private byte[] HuffmanCompress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            int[] freqs = new int[256];
            foreach (byte b in data) freqs[b]++;

            var nodes = new List<CompressorHuffmanNode>();
            for (int i = 0; i < 256; i++)
            {
                if (freqs[i] > 0)
                    nodes.Add(new CompressorHuffmanNode((byte)i, freqs[i]));
            }

            while (nodes.Count > 1)
            {
                nodes.Sort((a, b) => a.Freq.CompareTo(b.Freq));
                var left = nodes[0];
                var right = nodes[1];
                nodes.RemoveRange(0, 2);
                nodes.Add(new CompressorHuffmanNode(left, right));
            }

            if (nodes.Count == 0) return Array.Empty<byte>();
            var root = nodes[0];

            var codes = new Dictionary<byte, bool[]>();
            BuildCodes(root, new List<bool>(), codes);

            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);

            WriteTree(root, writer);

            writer.Write(data.Length);

            using var bitStream = new CompressorBitStream(output, CompressorBitStreamEndianness.Msb, CompressorBitStreamMode.Write);
            foreach (byte b in data)
            {
                if (codes.TryGetValue(b, out bool[]? code) && code != null)
                {
                    foreach (bool bit in code)
                    {
                        bitStream.WriteBit(bit);
                    }
                }
            }

            return output.ToArray();
        }

        private void BuildCodes(CompressorHuffmanNode node, List<bool> path, Dictionary<byte, bool[]> codes)
        {
            if (node.Left == null && node.Right == null)
            {
                codes[node.Value] = path.ToArray();
                return;
            }

            if (node.Left != null)
            {
                path.Add(false);
                BuildCodes(node.Left, path, codes);
                path.RemoveAt(path.Count - 1);
            }

            if (node.Right != null)
            {
                path.Add(true);
                BuildCodes(node.Right, path, codes);
                path.RemoveAt(path.Count - 1);
            }
        }

        private void WriteTree(CompressorHuffmanNode node, BinaryWriter writer)
        {
            if (node.Left == null && node.Right == null)
            {
                writer.Write((byte)0);
                writer.Write(node.Value);
            }
            else
            {
                writer.Write((byte)1);
                if (node.Left != null) WriteTree(node.Left, writer);
                if (node.Right != null) WriteTree(node.Right, writer);
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }

    public class CompressorHuffmanNode
    {
        public byte Value { get; }
        public int Freq { get; }
        public CompressorHuffmanNode? Left { get; }
        public CompressorHuffmanNode? Right { get; }

        public CompressorHuffmanNode(byte value, int freq)
        {
            Value = value;
            Freq = freq;
            Left = null;
            Right = null;
        }

        public CompressorHuffmanNode(CompressorHuffmanNode left, CompressorHuffmanNode right)
        {
            Value = 0;
            Freq = left.Freq + right.Freq;
            Left = left;
            Right = right;
        }
    }

    public class CompressorBitStream : IDisposable
    {
        private Stream stream;
        private CompressorBitStreamEndianness endianness;
        private CompressorBitStreamMode mode;
        private byte currentByte;
        private int bitPosition;

        public CompressorBitStream(Stream stream, CompressorBitStreamEndianness endianness, CompressorBitStreamMode mode)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.endianness = endianness;
            this.mode = mode;
            currentByte = 0;
            bitPosition = mode == CompressorBitStreamMode.Read ? 8 : 0;
        }

        public void WriteBit(bool bit)
        {
            int shift = endianness == CompressorBitStreamEndianness.Msb ? 7 - bitPosition : bitPosition;
            currentByte = (byte)((currentByte & ~(1 << shift)) | ((bit ? 1 : 0) << shift));
            bitPosition++;

            if (bitPosition == 8)
            {
                stream.WriteByte(currentByte);
                currentByte = 0;
                bitPosition = 0;
            }
        }

        public void Dispose()
        {
            if (mode == CompressorBitStreamMode.Write && bitPosition > 0)
            {
                stream.WriteByte(currentByte);
            }
        }
    }

    public enum CompressorBitStreamEndianness { Msb, Lsb }
    public enum CompressorBitStreamMode { Read, Write }
}
