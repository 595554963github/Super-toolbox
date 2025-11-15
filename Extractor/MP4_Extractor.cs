namespace super_toolbox
{
    public class MP4_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] HEADER_SEQ_1 = { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        private static readonly byte[] HEADER_SEQ_2 = { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 };
        private static readonly byte[] HEADER_SEQ_3 = { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70 };

        private static readonly byte[][] ALL_HEADER_SEQUENCES = { HEADER_SEQ_1, HEADER_SEQ_2, HEADER_SEQ_3 };

        private static readonly HashSet<string> VALID_BRANDS = new HashSet<string>
        {
            "mp41", "mp42", "isom", "avc1", "M4V ", "M4A ", "M4P ", "M4B ", "qt  ", "iso2", "3g2a", "drc1", "F4V", "F4P", "F4A", "F4B", "mmp4"
        };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase));
            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");
                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    int index = 0;
                    int innerCount = 1;
                    while (index < content.Length)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        int headerStartIndex = FindAnyHeader(content, index);
                        if (headerStartIndex == -1) break;
                        if (IsValidBrand(content, headerStartIndex))
                        {
                            int nextHeaderStart = FindAnyHeader(content, headerStartIndex + 1);
                            int segmentEnd = nextHeaderStart != -1 ? nextHeaderStart : content.Length;

                            ProcessMp4Segment(content, headerStartIndex, segmentEnd, filePath, innerCount, extractedDir, extractedFiles, cancellationToken);
                            innerCount++;
                            index = segmentEnd;
                        }
                        else
                        {
                            index = headerStartIndex + 1;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (IOException e)
                {
                    ExtractionError?.Invoke(this, $"读取文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"读取文件{filePath}时出错:{e.Message}");
                }
            }
            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个MP4文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到MP4文件");
            }
            OnExtractionCompleted();
        }
        private int FindAnyHeader(byte[] content, int startIndex)
        {
            int minIndex = int.MaxValue;
            foreach (var header in ALL_HEADER_SEQUENCES)
            {
                int index = IndexOf(content, header, startIndex);
                if (index != -1 && index < minIndex)
                {
                    minIndex = index;
                }
            }
            return minIndex == int.MaxValue ? -1 : minIndex;
        }
        private bool IsValidBrand(byte[] content, int headerStartIndex)
        {
            if (headerStartIndex + 12 >= content.Length)
                return false;

            string brand = System.Text.Encoding.ASCII.GetString(content, headerStartIndex + 8, 4);
            return VALID_BRANDS.Contains(brand);
        }
        private void ProcessMp4Segment(byte[] content, int start, int end, string filePath, int innerCount,
                                     string extractedDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int length = end - start;
            if (length <= 100)
                return;

            byte[] mp4Data = new byte[length];
            Array.Copy(content, start, mp4Data, 0, length);
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputFileName = $"{baseFileName}_{innerCount}.mp4";
            string outputFilePath = Path.Combine(extractedDir, outputFileName);

            if (File.Exists(outputFilePath))
            {
                int duplicateCount = 1;
                do
                {
                    outputFileName = $"{baseFileName}_{innerCount}_dup{duplicateCount}.mp4";
                    outputFilePath = Path.Combine(extractedDir, outputFileName);
                    duplicateCount++;
                    ThrowIfCancellationRequested(cancellationToken);
                } while (File.Exists(outputFilePath));
            }
            try
            {
                File.WriteAllBytes(outputFilePath, mp4Data);
                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                }
            }
            catch (IOException e)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                OnExtractionFailed($"写入文件{outputFilePath}时出错:{e.Message}");
            }
        }
        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}