namespace super_toolbox
{
    public class Lzs_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private const int EI = 12;
        private const int EJ = 4;
        private const int P = 2;
        private const int rless = 2;
        private const int init_chr = 0;

        private static byte[]? slide_win = null;
        private static int slide_winsz = 0;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath} 不是有效的目录");
                return;
            }

            var lzsFiles = Directory.GetFiles(directoryPath, "*.lzs", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    if (int.TryParse(fileName, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToList();

            TotalFilesToExtract = lzsFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{TotalFilesToExtract}个LZS文件");

            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var lzsFilePath in lzsFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string lzsFileName = Path.GetFileName(lzsFilePath);
                            string extractDir = Path.GetDirectoryName(lzsFilePath) ?? "";

                            ExtractionProgress?.Invoke(this, $"正在处理:{lzsFileName}");

                            int extractedCount = DecompressLzsFile(lzsFilePath, extractDir);
                            totalExtractedFiles += extractedCount;

                            ExtractionProgress?.Invoke(this, $"完成处理:{lzsFileName}->{extractedCount}个文件");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(lzsFilePath)}时出错:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"提取完成,总共提取了{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        private int DecompressLzsFile(string filePath, string outputDir)
        {
            byte[] compressedData = File.ReadAllBytes(filePath);

            if (compressedData.Length < 4)
            {
                ExtractionError?.Invoke(this, $"文件太小:{filePath}");
                return 0;
            }

            uint uncompressedSize = (uint)(compressedData[0] | (compressedData[1] << 8) | (compressedData[2] << 16) | (compressedData[3] << 24));
            int compressedSize = compressedData.Length - 4;

            byte[] compressedPart = new byte[compressedSize];
            Array.Copy(compressedData, 4, compressedPart, 0, compressedSize);

            byte[] uncompressedData = new byte[uncompressedSize];
            int result = Unlzss(compressedPart, compressedSize, uncompressedData, (int)uncompressedSize);

            if (result < 0 || result != uncompressedSize)
            {
                ExtractionError?.Invoke(this, $"解压失败:{filePath}");
                return 0;
            }

            string outputFileName = Path.GetFileNameWithoutExtension(filePath) + ".dcmp";
            string outputPath = Path.Combine(outputDir, outputFileName);
            File.WriteAllBytes(outputPath, uncompressedData);

            OnFileExtracted(outputFileName);
            ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");

            return 1;
        }

        private int Unlzss(byte[] src, int srclen, byte[] dst, int dstlen)
        {
            int N = 1 << EI;
            int F = 1 << EJ;

            if (slide_win == null || N > slide_winsz)
            {
                slide_win = new byte[N];
                slide_winsz = N;
            }

            for (int i = 0; i < N; i++)
            {
                slide_win[i] = (byte)init_chr;
            }

            int dststart = 0;
            int dstpos = 0;
            int srcpos = 0;
            int r = (N - F) - rless;
            int Nmask = N - 1;
            int Fmask = F - 1;
            uint flags = 0;

            while (srcpos < srclen && dstpos < dstlen)
            {
                if ((flags & 0x100) == 0)
                {
                    if (srcpos >= srclen) break;
                    flags = (uint)(src[srcpos++] | 0xFF00);
                }

                if ((flags & 1) != 0)
                {
                    if (srcpos >= srclen) break;
                    byte c = src[srcpos++];
                    if (dstpos >= dstlen) return -1;
                    dst[dstpos++] = c;
                    slide_win[r] = c;
                    r = (r + 1) & Nmask;
                }
                else
                {
                    if (srcpos >= srclen) break;
                    int i = src[srcpos++];
                    if (srcpos >= srclen) break;
                    int j = src[srcpos++];
                    i |= ((j >> EJ) << 8);
                    j = (j & Fmask) + P;

                    for (int k = 0; k <= j; k++)
                    {
                        if (dstpos >= dstlen) return -1;
                        int pos = (i + k) & Nmask;
                        byte c = slide_win[pos];
                        dst[dstpos++] = c;
                        slide_win[r] = c;
                        r = (r + 1) & Nmask;
                    }
                }

                flags >>= 1;
            }

            return dstpos - dststart;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}