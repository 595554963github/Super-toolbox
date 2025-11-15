namespace super_toolbox
{
    public class DataDatExtractor : BaseExtractor
    {
        private static readonly byte[] PNG_START_SEQ = { 0x89, 0x50, 0x4E, 0x47 };
        private static readonly byte[] PNG_BLOCK_MARKER = { 0x49, 0x48, 0x44, 0x52 };
        private static readonly byte[] PNG_END_SEQ = { 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };
        private static readonly byte[] AT3_START_SEQ = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] AT3_WAVE_MARKER = { 0x57, 0x41, 0x56, 0x45 };
        private static readonly byte[] AT3_FMT_MARKER = { 0x66, 0x6D, 0x74, 0x20 };
        private static readonly byte[] PMF_SIGNATURE = { 0x50, 0x53, 0x4D, 0x46, 0x30, 0x30 };
        private const string IMAGE_FOLDER = "Images";
        private const string AUDIO_FOLDER = "Audio";
        private const string VIDEO_FOLDER = "Video";
        private int pmfCounter = 0;
        private int pngCounter = 0;
        private int at3Counter = 0;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误: {directoryPath} 不是有效的目录");
                return;
            }

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            string imageDir = Path.Combine(extractedDir, IMAGE_FOLDER);
            string audioDir = Path.Combine(extractedDir, AUDIO_FOLDER);
            string videoDir = Path.Combine(extractedDir, VIDEO_FOLDER);

            Directory.CreateDirectory(imageDir);
            Directory.CreateDirectory(audioDir);
            Directory.CreateDirectory(videoDir);

            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
               .Where(file => !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                      !file.EndsWith(".at3", StringComparison.OrdinalIgnoreCase) &&
                      !file.EndsWith(".pmf", StringComparison.OrdinalIgnoreCase))
               .ToList();
            TotalFilesToExtract = files.Count;
            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    }, filePath =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            ExtractPMF(filePath, videoDir);
                            ExtractPNG(filePath, imageDir);
                            ExtractAT3(filePath, audioDir);
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"处理文件 {Path.GetFileName(filePath)} 时出错: {ex.Message}");
                        }
                    });
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取失败: {ex.Message}");
            }
        }
        private bool ValidatePMF(byte[] data)
        {
            return data.Length >= 6 && data.Take(6).SequenceEqual(PMF_SIGNATURE);
        }
        private void ExtractPMF(string filePath, string outputDir)
        {
            byte[] content = File.ReadAllBytes(filePath);
            int pos = 0;
            while (pos < content.Length)
            {
                int pmfStart = IndexOf(content, PMF_SIGNATURE, pos);
                if (pmfStart == -1) break;

                int nextPmf = IndexOf(content, PMF_SIGNATURE, pmfStart + 6);
                int nextPng = IndexOf(content, PNG_START_SEQ, pmfStart + 6);
                int nextAt3 = IndexOf(content, AT3_START_SEQ, pmfStart + 6);

                List<int> endPositions = new List<int>();
                if (nextPmf != -1) endPositions.Add(nextPmf);
                if (nextPng != -1) endPositions.Add(nextPng);
                if (nextAt3 != -1) endPositions.Add(nextAt3);

                int pmfEnd = endPositions.Count > 0 ? endPositions.Min() : content.Length;
                byte[] pmfData = new byte[pmfEnd - pmfStart];
                Array.Copy(content, pmfStart, pmfData, 0, pmfData.Length);

                if (pmfData.Length > 6 && ValidatePMF(pmfData))
                {
                    string outPath = Path.Combine(outputDir, $"data{Interlocked.Increment(ref pmfCounter)}.pmf");
                    CreateDirectoryIfNotExists(outPath);
                    File.WriteAllBytes(outPath, pmfData);
                    OnFileExtracted(outPath);
                }
                pos = pmfEnd;
            }
        }
        private void ExtractPNG(string filePath, string outputDir)
        {
            byte[] content = File.ReadAllBytes(filePath);
            byte[] leftover = Array.Empty<byte>();
            byte[] currentPng = Array.Empty<byte>();
            bool foundStart = false;

            for (int index = 0; index < content.Length;)
            {
                byte[] chunk = new byte[Math.Min(8192, content.Length - index)];
                Array.Copy(content, index, chunk, 0, chunk.Length);

                byte[] data = leftover.Concat(chunk).ToArray();

                if (!foundStart)
                {
                    int startIdx = IndexOf(data, PNG_START_SEQ, 0);
                    if (startIdx != -1)
                    {
                        foundStart = true;
                        currentPng = data.Skip(startIdx).ToArray();
                        leftover = Array.Empty<byte>();
                    }
                    else
                    {
                        leftover = data.Length >= PNG_START_SEQ.Length ?
                            data.Skip(data.Length - PNG_START_SEQ.Length + 1).ToArray() :
                            data;
                    }
                }
                else
                {
                    currentPng = currentPng.Concat(chunk).ToArray();
                    int endIdx = IndexOf(currentPng, PNG_END_SEQ, 0);
                    if (endIdx != -1)
                    {
                        endIdx += PNG_END_SEQ.Length;
                        byte[] extracted = currentPng.Take(endIdx).ToArray();

                        if (IndexOf(extracted, PNG_BLOCK_MARKER, 0) != -1 && ValidatePNG(extracted))
                        {
                            string outPath = Path.Combine(outputDir, $"data{Interlocked.Increment(ref pngCounter)}.png");
                            CreateDirectoryIfNotExists(outPath);
                            File.WriteAllBytes(outPath, extracted);
                            OnFileExtracted(outPath);
                        }
                        foundStart = false;
                        leftover = currentPng.Skip(endIdx).ToArray();
                        currentPng = Array.Empty<byte>();
                    }
                }
                index += chunk.Length;
            }
        }
        private void ExtractAT3(string filePath, string outputDir)
        {
            byte[] content = File.ReadAllBytes(filePath);
            int waveDataStart = 0;
            while (true)
            {
                waveDataStart = IndexOf(content, AT3_START_SEQ, waveDataStart);
                if (waveDataStart == -1) break;

                if (waveDataStart + 12 > content.Length)
                {
                    waveDataStart += 4;
                    continue;
                }
                try
                {
                    int fileSize = BitConverter.ToInt32(content, waveDataStart + 4);
                    fileSize = (fileSize + 1) & ~1;

                    if (fileSize <= 0 || waveDataStart + 8 + fileSize > content.Length)
                    {
                        waveDataStart += 4;
                        continue;
                    }

                    int blockStart = waveDataStart + 8;
                    bool hasWave = IndexOf(content, AT3_WAVE_MARKER, blockStart, blockStart + fileSize) != -1;
                    bool hasFmt = IndexOf(content, AT3_FMT_MARKER, blockStart, blockStart + fileSize) != -1;

                    if (hasWave && hasFmt)
                    {
                        byte[] extracted = new byte[8 + fileSize];
                        Array.Copy(content, waveDataStart, extracted, 0, 8 + fileSize);

                        if (ValidateAT3(extracted))
                        {
                            string outPath = Path.Combine(outputDir, $"data{Interlocked.Increment(ref at3Counter)}.at3");
                            CreateDirectoryIfNotExists(outPath);
                            File.WriteAllBytes(outPath, extracted);
                            OnFileExtracted(outPath);
                        }

                        waveDataStart += 8 + fileSize;
                    }
                    else
                    {
                        waveDataStart += 4;
                    }
                }
                catch
                {
                    waveDataStart += 4;
                }
            }
        }
        private bool ValidatePNG(byte[] data)
        {
            return data.Length >= 8 && data.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        }
        private bool ValidateAT3(byte[] data)
        {
            return data.Length >= 12 && data.Take(4).SequenceEqual(AT3_START_SEQ) && data.Skip(8).Take(4).SequenceEqual(AT3_WAVE_MARKER);
        }
        private static int IndexOf(byte[] data, byte[] pattern, int startIndex, int endIndex = -1)
        {
            if (endIndex == -1) endIndex = data.Length;
            for (int i = startIndex; i <= endIndex - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
        private void CreateDirectoryIfNotExists(string filePath)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}