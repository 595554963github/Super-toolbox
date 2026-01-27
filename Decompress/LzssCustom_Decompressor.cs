namespace super_toolbox
{
    public class LzssCustom_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        private const int LOOKAHEAD_BUFFER_SIZE = 264;
        private const int MIN_LENGTH = 4;
        private const int HISTORY_SIZE = 65536;
        private const int NUM_BITS_LOOKAHEAD = 8;
        private const int NUM_BITS_HISTORY = 16;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsLzssFile).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的LZSS压缩文件");
                        OnDecompressionFailed("未找到有效的LZSS压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压,共{TotalFilesToDecompress}个文件");

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DecompressLzssFile(filePath, decompressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, fileName);
                            OnFileDecompressed(outputPath);
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
                DecompressionError?.Invoke(this, $"解压失败:{ex.Message}");
                OnDecompressionFailed($"解压失败:{ex.Message}");
            }
        }

        private bool IsLzssFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lzss";
        }

        private bool DecompressLzssFile(string inputPath, string outputDir)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(inputPath);
                string outputPath = Path.Combine(outputDir, fileName);

                DecompressionProgress?.Invoke(this, $"正在解压:{Path.GetFileName(inputPath)}");

                byte[] compressedData = File.ReadAllBytes(inputPath);
                byte[] decompressedData = Decompress(compressedData);

                File.WriteAllBytes(outputPath, decompressedData);

                if (File.Exists(outputPath))
                {
                    DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)}");
                    return true;
                }
                else
                {
                    DecompressionError?.Invoke(this, $"解压成功但输出文件不存在:{outputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"LZSS解压错误:{ex.Message}");
                return false;
            }
        }

        private byte[] Decompress(byte[] compressedData)
        {
            using (MemoryStream inputStream = new MemoryStream(compressedData))
            {
                LZSSDecoder decoder = new LZSSDecoder();
                return decoder.Decode(inputStream);
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private class LookAheadBuffer
        {
            private byte[] data = new byte[LOOKAHEAD_BUFFER_SIZE];
            private int pos = 0;
            private int size = 0;

            public void Add(int c)
            {
                if (c != -1)
                {
                    data[pos] = (byte)c;
                    if (size < LOOKAHEAD_BUFFER_SIZE)
                        size++;
                }
                else
                {
                    size--;
                }
                pos = (pos + 1) % LOOKAHEAD_BUFFER_SIZE;
            }

            public byte At(int i) => data[i];
            public int Size => size;
            public int Pos => pos;
            public byte First() => data[pos];
            public void ResetPos() => pos = 0;
        }

        private class HistoryBuffer
        {
            private byte[] data = new byte[HISTORY_SIZE];
            private int pos = 0;

            public void Add(int c)
            {
                data[pos] = (byte)c;
                pos = (pos + 1) & (HISTORY_SIZE - 1);
            }

            public byte At(int i) => data[i & (HISTORY_SIZE - 1)];
            public int Pos => pos;
        }

        private class LZSSDecoder
        {
            private LookAheadBuffer lookAhead = new LookAheadBuffer();
            private HistoryBuffer history = new HistoryBuffer();

            public byte[] Decode(Stream inputStream)
            {
                BitInputStream bitStream = new BitInputStream(inputStream);
                try
                {
                    // 读取原始文件大小（32位，小端序）
                    ulong fileSizeBits = bitStream.GetBits(32);
                    long fsize = (long)fileSizeBits;

                    using (MemoryStream outputStream = new MemoryStream((int)fsize))
                    {
                        while (fsize > 0)
                        {
                            if (bitStream.GetBits(1) == 0)
                            {
                                int c = (int)bitStream.GetBits(8);
                                outputStream.WriteByte((byte)c);
                                history.Add(c);
                                fsize--;
                            }
                            else
                            {
                                long position = (long)bitStream.GetBits(NUM_BITS_HISTORY);
                                long length = (long)bitStream.GetBits(NUM_BITS_LOOKAHEAD) + MIN_LENGTH;

                                for (long i = 0; i < length; i++)
                                {
                                    int c = history.At((int)((position + i) & (HISTORY_SIZE - 1)));
                                    lookAhead.Add(c);
                                    outputStream.WriteByte((byte)c);
                                    fsize--;
                                }

                                lookAhead.ResetPos();
                                for (long i = 0; i < length; i++)
                                {
                                    history.Add(lookAhead.At((int)i));
                                }
                            }
                        }

                        return outputStream.ToArray();
                    }
                }
                finally
                {
                    ((IDisposable)bitStream).Dispose();
                }
            }
        }

        private class BitInputStream : IDisposable
        {
            private Stream inStream;
            private int bitCount = 0;
            private int buffer = 0;

            public BitInputStream(Stream inStream)
            {
                this.inStream = inStream;
            }

            public int GetBit()
            {
                if (bitCount == 0)
                {
                    int b = inStream.ReadByte();
                    if (b == -1)
                        return 0;
                    buffer = b;
                    bitCount = 8;
                }
                return (buffer >> --bitCount) & 1;
            }

            public ulong GetBits(int size)
            {
                ulong value = 0;
                for (int i = 0; i < size; i++)
                {
                    value = (value << 1) | (byte)(GetBit() & 1);
                }
                return value;
            }

            public void Dispose()
            {
            }
        }
    }
}
