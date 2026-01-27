namespace super_toolbox
{
    public class LzssCustom_Compressor : BaseExtractor
    {
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        private const int LOOKAHEAD_BUFFER_SIZE = 264;
        private const int MIN_LENGTH = 4;
        private const int HISTORY_SIZE = 65536;
        private const int NUM_BITS_LOOKAHEAD = 8;
        private const int NUM_BITS_HISTORY = 16;
        private const int HASH_SIZE = 65536;
        private const int SEARCH_DEPTH = 512;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsNotLzssFile).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        CompressionError?.Invoke(this, "未找到需要压缩的文件");
                        OnCompressionFailed("未找到需要压缩的文件");
                        return;
                    }

                    string compressedDir = Path.Combine(directoryPath, "Compressed");
                    Directory.CreateDirectory(compressedDir);
                    TotalFilesToCompress = filesToProcess.Length;
                    CompressionStarted?.Invoke(this, $"开始压缩,共{TotalFilesToCompress}个文件");

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (CompressLzssFile(filePath, compressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputPath = Path.Combine(compressedDir, fileName + ".lzss");
                            OnFileCompressed(outputPath);
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
                CompressionError?.Invoke(this, $"压缩失败:{ex.Message}");
                OnCompressionFailed($"压缩失败:{ex.Message}");
            }
        }

        private bool IsNotLzssFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension != ".lzss";
        }

        private bool CompressLzssFile(string inputPath, string outputDir)
        {
            try
            {
                string fileName = Path.GetFileName(inputPath);
                string outputPath = Path.Combine(outputDir, fileName + ".lzss");

                CompressionProgress?.Invoke(this, $"正在压缩:{fileName}");

                byte[] originalData = File.ReadAllBytes(inputPath);
                byte[] compressedData = Compress(originalData);

                File.WriteAllBytes(outputPath, compressedData);

                if (File.Exists(outputPath))
                {
                    CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)}");
                    return true;
                }
                else
                {
                    CompressionError?.Invoke(this, $"压缩成功但输出文件不存在:{outputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"LZSS压缩错误:{ex.Message}");
                return false;
            }
        }

        private byte[] Compress(byte[] inputData)
        {
            using (MemoryStream outputStream = new MemoryStream())
            {
                LZSSEncoder encoder = new LZSSEncoder();
                encoder.Encode(inputData, outputStream);
                return outputStream.ToArray();
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

            public byte At(int i) => data[i % LOOKAHEAD_BUFFER_SIZE];
            public int Size => size;
            public int Pos => pos;
            public byte First() => data[pos];

            public void ResetPos() => pos = 0;

            public long GetHash()
            {
                uint a = data[pos];
                uint b = data[(pos + 1) % LOOKAHEAD_BUFFER_SIZE];
                uint c = data[(pos + 2) % LOOKAHEAD_BUFFER_SIZE];
                uint d = data[(pos + 3) % LOOKAHEAD_BUFFER_SIZE];
                return (long)(((d << 8) ^ (c << 5) + (b << 2)) + a) & (HASH_SIZE - 1);
            }
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

            public long GetHash()
            {
                uint a = data[pos];
                uint b = data[(pos + 1) & (HISTORY_SIZE - 1)];
                uint c = data[(pos + 2) & (HISTORY_SIZE - 1)];
                uint d = data[(pos + 3) & (HISTORY_SIZE - 1)];
                return (long)(((d << 8) ^ (c << 5) + (b << 2)) + a) & (HASH_SIZE - 1);
            }
        }

        private class Searcher
        {
            private class Node
            {
                public long prev = -1;
                public long next = -1;
            }

            private class HashEntry
            {
                public long first = -1;
                public long last = -1;
            }

            private Node[] nodes = new Node[HISTORY_SIZE];
            private HashEntry[] hashTable = new HashEntry[HASH_SIZE];
            private LookAheadBuffer lookAhead;
            private HistoryBuffer history;

            public Searcher(LookAheadBuffer lookAhead, HistoryBuffer history)
            {
                this.lookAhead = lookAhead;
                this.history = history;

                for (int i = 0; i < HISTORY_SIZE; i++)
                {
                    nodes[i] = new Node();
                }

                for (int i = 0; i < HASH_SIZE; i++)
                {
                    hashTable[i] = new HashEntry();
                }
            }

            private long Compare(long searchPos)
            {
                long i = 0;
                while (history.At((int)((searchPos + i) & (HISTORY_SIZE - 1))) ==
                       lookAhead.At((int)(lookAhead.Pos + i) % LOOKAHEAD_BUFFER_SIZE))
                {
                    i++;
                }
                return i;
            }

            public void FindMatch(out long position, out long length)
            {
                position = 0;
                length = MIN_LENGTH - 1;

                if (lookAhead.Size < MIN_LENGTH)
                    return;

                long hash = lookAhead.GetHash();
                long i = hashTable[hash].last;

                if (i == -1)
                    return;

                int cnt = 0;
                do
                {
                    if (history.At((int)((i + length) & (HISTORY_SIZE - 1))) ==
                        lookAhead.At((int)(lookAhead.Pos + length) % LOOKAHEAD_BUFFER_SIZE))
                    {
                        long l = Compare(i);
                        if (l > length)
                        {
                            position = i;
                            length = l;
                        }
                    }
                    i = nodes[i].prev;
                } while (i != -1 && cnt++ < SEARCH_DEPTH);

                if (length > LOOKAHEAD_BUFFER_SIZE - 1)
                    length = LOOKAHEAD_BUFFER_SIZE - 1;
            }

            public void Add()
            {
                long hash = lookAhead.GetHash();
                long last = hashTable[hash].last;
                long currentPos = history.Pos;

                if (last == -1)
                {
                    hashTable[hash].first = currentPos;
                }
                else
                {
                    nodes[last].next = currentPos;
                }

                nodes[currentPos].prev = last;
                nodes[currentPos].next = -1;
                hashTable[hash].last = currentPos;
            }

            public void Remove()
            {
                long hash = history.GetHash();
                long removePos = hashTable[hash].first;

                if (removePos != -1)
                {
                    long next = nodes[removePos].next;
                    if (next != -1)
                    {
                        nodes[next].prev = -1;
                    }
                    else
                    {
                        hashTable[hash].last = -1;
                    }

                    nodes[removePos].next = -1;
                    hashTable[hash].first = next;
                }
            }
        }

        private class LZSSEncoder
        {
            private LookAheadBuffer lookAhead = new LookAheadBuffer();
            private HistoryBuffer history = new HistoryBuffer();
            private Searcher searcher;

            public LZSSEncoder()
            {
                searcher = new Searcher(lookAhead, history);
            }

            public void Encode(byte[] inputData, Stream outputStream)
            {
                BitOutputStream bitStream = new BitOutputStream(outputStream);
                try
                {
                    // 写入原始文件大小（32位，小端序）
                    uint fileSize = (uint)inputData.Length;
                    bitStream.PutBits(fileSize, 32);

                    int inputPos = 0;

                    // 初始化lookahead buffer
                    while (lookAhead.Size < LOOKAHEAD_BUFFER_SIZE && inputPos < inputData.Length)
                    {
                        lookAhead.Add(inputData[inputPos++]);
                    }
                    lookAhead.ResetPos();

                    while (lookAhead.Size > 0)
                    {
                        searcher.FindMatch(out long position, out long length);

                        if (length < MIN_LENGTH)
                        {
                            // 写入标志位0 + 单个字符
                            bitStream.PutBits(0, 1);
                            bitStream.PutBits(lookAhead.First(), 8);

                            searcher.Remove();
                            searcher.Add();
                            history.Add(lookAhead.First());
                            lookAhead.Add(inputPos < inputData.Length ? inputData[inputPos++] : -1);
                        }
                        else
                        {
                            // 写入标志位1 + (位置, 长度)对
                            bitStream.PutBits(1, 1);
                            bitStream.PutBits((ulong)position, NUM_BITS_HISTORY);
                            bitStream.PutBits((ulong)(length - MIN_LENGTH), NUM_BITS_LOOKAHEAD);

                            for (long k = length; k > 0; k--)
                            {
                                searcher.Remove();
                                searcher.Add();
                                history.Add(lookAhead.First());
                                lookAhead.Add(inputPos < inputData.Length ? inputData[inputPos++] : -1);
                            }
                        }
                    }

                    bitStream.FlushBits();
                }
                finally
                {
                    ((IDisposable)bitStream).Dispose();
                }
            }
        }

        private class BitOutputStream : IDisposable
        {
            private Stream outStream;
            private int bitCount = 0;
            private int buffer = 0;

            public BitOutputStream(Stream outStream)
            {
                this.outStream = outStream;
            }

            public void PutBit(int bit)
            {
                buffer = (buffer << 1) | (bit & 1);
                if (++bitCount == 8)
                {
                    outStream.WriteByte((byte)buffer);
                    bitCount = 0;
                    buffer = 0;
                }
            }

            public void PutBits(ulong bits, int size)
            {
                for (int i = size - 1; i >= 0; i--)
                    PutBit((int)((bits >> i) & 1));
            }

            public void FlushBits()
            {
                while (bitCount > 0)
                    PutBit(0);
            }

            public void Dispose()
            {
                FlushBits();
                outStream.Flush();
            }
        }
    }
}
