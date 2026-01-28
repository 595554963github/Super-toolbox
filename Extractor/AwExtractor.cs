using System.Text;

namespace super_toolbox
{
    public class AwExtractor : BaseExtractor
    {
        private readonly short[,] afccoef = new short[16, 2]
        {
        {0, 0}, {2048, 0}, {0, 2048}, {1024, 1024},
        {4096, -2048}, {3584, -1536}, {3072, -1024}, {4608, -2560},
        {4200, -2248}, {4800, -2300}, {5120, -3072}, {2048, -2048},
        {1024, -1024}, {-1024, 1024}, {-1024, 0}, {-2048, 0}
        };

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录 {directoryPath} 不存在");
                OnExtractionFailed($"错误:目录 {directoryPath} 不存在");
                return;
            }

            var awFiles = Directory.GetFiles(directoryPath, "*.aw");
            if (awFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.aw文件,无法执行解包");
                OnExtractionFailed("未找到.aw文件,无法执行解包");
                return;
            }

            var wsysFiles = Directory.GetFiles(directoryPath, "*.wsys");
            if (wsysFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.wsys文件,无法执行解包");
                OnExtractionFailed("未找到.wsys文件,无法执行解包");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");
            ExtractionProgress?.Invoke(this, $"找到{awFiles.Length}个.aw文件和{wsysFiles.Length}个.wsys文件");

            string extractDir = Path.Combine(directoryPath, "Extracted");
            if (!Directory.Exists(extractDir))
            {
                Directory.CreateDirectory(extractDir);
            }

            TotalFilesToExtract = 0;

            try
            {
                await Task.Run(() =>
                {
                    int totalFiles = 0;
                    foreach (var wsysFilePath in wsysFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string fileName = Path.GetFileName(wsysFilePath);
                        ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                        int filesExtracted = DoWSYS(wsysFilePath, extractDir, cancellationToken);
                        totalFiles += filesExtracted;

                        ExtractionProgress?.Invoke(this, $"从{fileName}提取出{filesExtracted}个.wav文件");
                    }

                    var finalFiles = Directory.GetFiles(extractDir, "*.wav");
                    ExtractionProgress?.Invoke(this, $"提取完成,共提取{finalFiles.Length}个.wav文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理失败:{ex.Message}");
                OnExtractionFailed($"处理失败:{ex.Message}");
            }
        }

        private int DoWSYS(string wsysFilePath, string extractDir, CancellationToken cancellationToken)
        {
            int filesExtracted = 0;

            using (var infile = new FileStream(wsysFilePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(infile))
            {
                if (!CompareBytes(reader.ReadBytes(4), Encoding.ASCII.GetBytes("WSYS")))
                {
                    ExtractionError?.Invoke(this, $"{wsysFilePath}不是有效的WSYS文件");
                    return 0;
                }

                infile.Position += 12;

                int winfOffset = Read32BE(reader) + 0;

                infile.Position = winfOffset;

                if (!CompareBytes(reader.ReadBytes(4), Encoding.ASCII.GetBytes("WINF")))
                {
                    ExtractionError?.Invoke(this, $"WINF标签在偏移{winfOffset:X}处未找到");
                    return 0;
                }

                int awCount = Read32BE(reader);
                ExtractionProgress?.Invoke(this, $"{awCount}个aw条目");

                for (int aw_i = 0; aw_i < awCount; aw_i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    infile.Position = winfOffset + 8 + (aw_i * 4);
                    int awNameOffset = Read32BE(reader) + 0;
                    int tableOffset = awNameOffset + 112;

                    infile.Position = awNameOffset;

                    byte[] fnameBytes = reader.ReadBytes(112);
                    string fname = Encoding.ASCII.GetString(fnameBytes).TrimEnd('\0');

                    string awFilePath = Path.Combine(Path.GetDirectoryName(wsysFilePath) ?? string.Empty, fname);

                    if (!File.Exists(awFilePath))
                    {
                        ExtractionError?.Invoke(this, $"{fname}未找到");
                        continue;
                    }

                    using (var awfile = new FileStream(awFilePath, FileMode.Open, FileAccess.Read))
                    using (var awReader = new BinaryReader(awfile))
                    {
                        int wavCount = Read32BE(reader);

                        ExtractionProgress?.Invoke(this, $"aw={fname}");

                        for (int i = 0; i < wavCount; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            infile.Position = tableOffset + 4 + i * 4;
                            int wavEntryOffset = Read32BE(reader) + 0;

                            infile.Position = wavEntryOffset + 4;

                            byte[] srateBytes = reader.ReadBytes(4);
                            int srate = ((srateBytes[1] << 8) | srateBytes[2]) / 2;

                            int afcoffset = Read32BE(reader);
                            int afcsize = Read32BE(reader);
                            infile.Position += 4;

                            string awName = Path.GetFileNameWithoutExtension(fname);
                            string outPath = Path.Combine(extractDir, $"{awName}_{i + 1}.wav");

                            if (DumpAFC(awfile, afcoffset, afcsize, srate, outPath))
                            {
                                OnFileExtracted(outPath);
                                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outPath)}");
                                filesExtracted++;
                            }
                        }
                    }
                }
            }
            return filesExtracted;
        }

        private bool DumpAFC(FileStream awfile, int offset, int size, int srate, string filename)
        {
            long oldPos = awfile.Position;

            try
            {
                awfile.Position = offset;

                short hist = 0, hist2 = 0;
                int outsize = size / 9 * 16 * 2;
                int outsizetotal = outsize + 8;

                using (var outfile = new FileStream(filename, FileMode.Create, FileAccess.Write))
                {
                    byte[] wavhead = new byte[44];
                    Array.Copy(Encoding.ASCII.GetBytes("RIFF"), 0, wavhead, 0, 4);
                    Write32LE(outsizetotal, wavhead, 4);
                    Array.Copy(Encoding.ASCII.GetBytes("WAVEfmt "), 0, wavhead, 8, 8);
                    Write32LE(16, wavhead, 16);
                    Write32LE(1, wavhead, 20);
                    Write32LE(1, wavhead, 22);
                    Write32LE(srate, wavhead, 24);
                    Write32LE(srate * 2, wavhead, 28);
                    Write32LE(2, wavhead, 32);
                    Write32LE(16, wavhead, 34);
                    Array.Copy(Encoding.ASCII.GetBytes("data"), 0, wavhead, 36, 4);
                    Write32LE(outsize, wavhead, 40);

                    outfile.Write(wavhead, 0, 44);

                    for (int sizeleft = size; sizeleft >= 9; sizeleft -= 9)
                    {
                        byte[] inbuf = new byte[9];
                        awfile.ReadExactly(inbuf, 0, 9);

                        short[] outbuf = AFCdecodebuffer(inbuf, ref hist, ref hist2);
                        byte[] outBytes = new byte[32];
                        Buffer.BlockCopy(outbuf, 0, outBytes, 0, 32);
                        outfile.Write(outBytes, 0, 32);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"写入{filename}失败: {ex.Message}");
                return false;
            }
            finally
            {
                awfile.Position = oldPos;
            }
        }

        private short[] AFCdecodebuffer(byte[] input, ref short hist, ref short hist2)
        {
            short[] outbuf = new short[16];
            short[] nibbles = new short[16];

            int delta = 1 << ((input[0] >> 4) & 0xF);
            int idx = input[0] & 0xF;

            int srcIndex = 1;
            for (int i = 0; i < 16; i += 2)
            {
                int j = (input[srcIndex] & 255) >> 4;
                nibbles[i] = (short)j;
                j = input[srcIndex] & 255 & 15;
                nibbles[i + 1] = (short)j;
                srcIndex++;
            }

            for (int i = 0; i < 16; i++)
            {
                if (nibbles[i] >= 8)
                    nibbles[i] = (short)(nibbles[i] - 16);
            }

            for (int i = 0; i < 16; i++)
            {
                int sample = (delta * nibbles[i]) << 11;
                sample += ((int)hist * afccoef[idx, 0]) + ((int)hist2 * afccoef[idx, 1]);
                sample = sample >> 11;

                if (sample > 32767) sample = 32767;
                if (sample < -32768) sample = -32768;

                outbuf[i] = (short)sample;
                hist2 = hist;
                hist = (short)sample;
            }

            return outbuf;
        }

        private int Read32BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private void Write32LE(int value, byte[] buffer, int offset)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private bool CompareBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
