using System.IO.MemoryMappedFiles;

namespace super_toolbox
{
    public class RIFF_RIFX_Sound_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] RIFF_HEADER = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] RIFX_HEADER = { 0x52, 0x49, 0x46, 0x58 };
        private static readonly byte[] WEM_BLOCK = { 0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74 };
        private static readonly byte[] XWMA_PATTERN = { 0x12, 0x00, 0x00, 0x00, 0x61, 0x01 };

        private const long LARGE_FILE_THRESHOLD = 2L * 1024 * 1024 * 1024; // 2GB
        private const long MEMORY_MAP_BUFFER_SIZE = 64 * 1024 * 1024; // 64MB

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

        private static bool ContainsBytes(byte[] data, byte[] pattern, int startIndex)
        {
            return IndexOf(data, pattern, startIndex) != -1;
        }

        private async Task<int> ProcessLargeFileWithMemoryMapAsync(string filePath, string extractedDir,
                                                                  List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            int waveCount = 0;

            using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
            {
                long fileSize = new FileInfo(filePath).Length;

                await ProcessLargeFileInBlocksAsync(
                    mmf,
                    fileSize,
                    async (accessor, currentOffset, blockSize, buffer) =>
                    {
                        int localExtractedCount = ProcessMemoryMapBlock(buffer, currentOffset, filePath,
                                                                       extractedDir, extractedFiles, ref waveCount);
                        extractedCount += localExtractedCount;
                        await Task.CompletedTask;
                    },
                    progressPercentage =>
                    {
                        ExtractionProgress?.Invoke(this, $"处理大文件:{Path.GetFileName(filePath)} - 进度{progressPercentage}%");
                    },
                    cancellationToken
                );
            }

            return extractedCount;
        }
        private async Task ProcessLargeFileInBlocksAsync(
            MemoryMappedFile mmf,
            long fileSize,
            Func<MemoryMappedViewAccessor, long, long, byte[], Task> processBlockFunc,
            Action<int>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            long currentOffset = 0;

            while (currentOffset < fileSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long blockSize = Math.Min(MEMORY_MAP_BUFFER_SIZE, fileSize - currentOffset);

                using (var accessor = mmf.CreateViewAccessor(currentOffset, blockSize, MemoryMappedFileAccess.Read))
                {
                    byte[] buffer = new byte[blockSize];
                    accessor.ReadArray(0, buffer, 0, buffer.Length);

                    await processBlockFunc(accessor, currentOffset, blockSize, buffer);
                }

                currentOffset += blockSize;

                if (progressCallback != null && fileSize > 0)
                {
                    int progressPercentage = (int)(currentOffset * 100 / fileSize);
                    progressCallback(progressPercentage);
                }
            }
        }

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
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedSourceFiles = 0;
            int totalExtractedFiles = 0;

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                processedSourceFiles++;

                ExtractionProgress?.Invoke(this, $"正在处理源文件({processedSourceFiles}/{TotalFilesToExtract}):{Path.GetFileName(filePath)}");

                try
                {
                    FileInfo fileInfo = new FileInfo(filePath);
                    int extractedCount = 0;

                    if (fileInfo.Length < LARGE_FILE_THRESHOLD)
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        extractedCount = ProcessFileContent(content, filePath, extractedDir, extractedFiles);
                    }
                    else
                    {
                        extractedCount = await ProcessLargeFileWithMemoryMapAsync(filePath, extractedDir, extractedFiles, cancellationToken);
                    }

                    totalExtractedFiles += extractedCount;
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
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{filePath}时发生错误:{e.Message}");
                    OnExtractionFailed($"处理文件{filePath}时发生错误:{e.Message}");
                }
            }

            if (totalExtractedFiles > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共处理{TotalFilesToExtract}个源文件,提取出{totalExtractedFiles}个音频文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共处理{TotalFilesToExtract}个源文件,未找到音频文件");
            }

            OnExtractionCompleted();
        }

        private int ProcessFileContent(byte[] content, string filePath, string extractedDir, List<string> extractedFiles)
        {
            int extractedCount = 0;
            int index = 0;
            int waveCount = 0;

            while (index < content.Length)
            {
                int riffStart = IndexOf(content, RIFF_HEADER, index);
                int rifxStart = IndexOf(content, RIFX_HEADER, index);
                int headerStart = -1;
                bool isRifx = false;

                if (riffStart != -1 && (rifxStart == -1 || riffStart < rifxStart))
                {
                    headerStart = riffStart;
                }
                else if (rifxStart != -1)
                {
                    headerStart = rifxStart;
                    isRifx = true;
                }
                else
                {
                    break;
                }

                if (isRifx)
                {
                    extractedCount += ProcessRifxContent(content, filePath, extractedDir, extractedFiles, ref index);
                    continue;
                }
                else
                {
                    extractedCount += ProcessRiffContent(content, filePath, extractedDir, extractedFiles, ref index, ref waveCount);
                }
            }

            return extractedCount;
        }

        private int ProcessMemoryMapBlock(byte[] buffer, long blockOffset, string filePath,
                                        string extractedDir, List<string> extractedFiles, ref int waveCount)
        {
            int extractedCount = 0;
            int index = 0;

            while (index < buffer.Length)
            {
                int riffStart = IndexOf(buffer, RIFF_HEADER, index);
                int rifxStart = IndexOf(buffer, RIFX_HEADER, index);
                int headerStart = -1;
                bool isRifx = false;

                if (riffStart != -1 && (rifxStart == -1 || riffStart < rifxStart))
                {
                    headerStart = riffStart;
                }
                else if (rifxStart != -1)
                {
                    headerStart = rifxStart;
                    isRifx = true;
                }
                else
                {
                    break;
                }

                if (isRifx)
                {
                    extractedCount += ProcessRifxMemoryMapBlock(buffer, blockOffset, filePath, extractedDir, extractedFiles, ref index);
                    continue;
                }
                else
                {
                    extractedCount += ProcessRiffMemoryMapBlock(buffer, blockOffset, filePath, extractedDir, extractedFiles, ref index, ref waveCount);
                }
            }

            return extractedCount;
        }

        private int ProcessRifxContent(byte[] content, string filePath, string extractedDir, List<string> extractedFiles, ref int index)
        {
            int extractedCount = 0;
            int? currentHeaderStart = null;
            int innerCount = 1;

            while (index < content.Length)
            {
                int headerStartIndex = IndexOf(content, RIFX_HEADER, index);
                if (headerStartIndex == -1)
                {
                    if (currentHeaderStart.HasValue)
                    {
                        if (ExtractRifxWaveFileWithSize(content, currentHeaderStart.Value, content.Length,
                                              filePath, innerCount, extractedDir, extractedFiles))
                        {
                            extractedCount++;
                        }
                    }
                    break;
                }

                int nextRifxIndex = IndexOf(content, RIFX_HEADER, headerStartIndex + 1);
                int endIndex = nextRifxIndex != -1 ? nextRifxIndex : content.Length;
                int blockStart = headerStartIndex + 8;

                bool hasWemBlock = ContainsBytes(content, WEM_BLOCK, blockStart);
                if (hasWemBlock)
                {
                    if (!currentHeaderStart.HasValue)
                    {
                        currentHeaderStart = headerStartIndex;
                    }
                    else
                    {
                        if (ExtractRifxWaveFileWithSize(content, currentHeaderStart.Value, headerStartIndex,
                                          filePath, innerCount, extractedDir, extractedFiles))
                        {
                            extractedCount++;
                        }
                        innerCount++;
                        currentHeaderStart = headerStartIndex;
                    }
                }

                index = headerStartIndex + 1;
            }

            if (currentHeaderStart.HasValue)
            {
                if (ExtractRifxWaveFileWithSize(content, currentHeaderStart.Value, content.Length,
                                  filePath, innerCount, extractedDir, extractedFiles))
                {
                    extractedCount++;
                }
            }

            return extractedCount;
        }

        private int ProcessRiffContent(byte[] content, string filePath, string extractedDir, List<string> extractedFiles, ref int index, ref int waveCount)
        {
            int extractedCount = 0;
            int headerStart = IndexOf(content, RIFF_HEADER, index);
            if (headerStart == -1)
            {
                index = content.Length;
                return extractedCount;
            }

            string format = "";
            bool hasValidFormat = false;

            if (IsValidWaveHeader(content, headerStart))
            {
                format = IdentifyAudioFormat(content, headerStart);
                hasValidFormat = !string.IsNullOrEmpty(format);

                if (hasValidFormat)
                {
                    waveCount++;
                    int fileSize = BitConverter.ToInt32(content, headerStart + 4);
                    int waveEnd = headerStart + 8 + fileSize;
                    if (waveEnd > content.Length)
                        waveEnd = content.Length;

                    int nextHeader = FindNextHeader(content, headerStart + 4);
                    if (nextHeader != -1 && nextHeader < waveEnd)
                        waveEnd = nextHeader;

                    if (ExtractWaveFileWithSize(content, headerStart, waveEnd, filePath, waveCount,
                                              extractedDir, extractedFiles, format))
                    {
                        extractedCount++;
                    }
                    index = waveEnd;
                }
                else
                {
                    index = headerStart + 4;
                }
            }
            else
            {
                if (headerStart + 22 < content.Length)
                {
                    if (content[headerStart + 8] == 0x58 && content[headerStart + 9] == 0x57 &&
                        content[headerStart + 10] == 0x4D && content[headerStart + 11] == 0x41)
                    {
                        bool matchesXwmaPattern = true;
                        for (int i = 0; i < 6; i++)
                        {
                            if (content[headerStart + 16 + i] != XWMA_PATTERN[i])
                            {
                                matchesXwmaPattern = false;
                                break;
                            }
                        }
                        if (matchesXwmaPattern)
                        {
                            format = "xwma";
                            hasValidFormat = true;

                            int fileSize = BitConverter.ToInt32(content, headerStart + 4);
                            int waveEnd = headerStart + 8 + fileSize;
                            if (waveEnd > content.Length)
                                waveEnd = content.Length;

                            waveCount++;
                            if (ExtractWaveFileWithSize(content, headerStart, waveEnd, filePath, waveCount,
                                                      extractedDir, extractedFiles, format))
                            {
                                extractedCount++;
                            }
                            index = waveEnd;
                        }
                    }
                }

                if (!hasValidFormat)
                {
                    index = headerStart + 4;
                }
            }

            return extractedCount;
        }

        private int ProcessRifxMemoryMapBlock(byte[] buffer, long blockOffset, string filePath,
                                            string extractedDir, List<string> extractedFiles, ref int index)
        {
            int extractedCount = 0;
            int? currentHeaderStart = null;
            int innerCount = 1;
            int currentIndex = index;

            while (currentIndex < buffer.Length)
            {
                int headerStartIndex = IndexOf(buffer, RIFX_HEADER, currentIndex);
                if (headerStartIndex == -1)
                {
                    if (currentHeaderStart.HasValue)
                    {
                        if (SaveRifxFromMemoryMap(buffer, currentHeaderStart.Value, buffer.Length,
                                                 blockOffset, filePath, innerCount, extractedDir, extractedFiles))
                        {
                            extractedCount++;
                        }
                    }
                    break;
                }

                int nextRifxIndex = IndexOf(buffer, RIFX_HEADER, headerStartIndex + 1);
                int endIndex = nextRifxIndex != -1 ? nextRifxIndex : buffer.Length;
                int blockStart = headerStartIndex + 8;

                bool hasWemBlock = ContainsBytes(buffer, WEM_BLOCK, blockStart);
                if (hasWemBlock)
                {
                    if (!currentHeaderStart.HasValue)
                    {
                        currentHeaderStart = headerStartIndex;
                    }
                    else
                    {
                        if (SaveRifxFromMemoryMap(buffer, currentHeaderStart.Value, headerStartIndex,
                                                 blockOffset, filePath, innerCount, extractedDir, extractedFiles))
                        {
                            extractedCount++;
                        }
                        innerCount++;
                        currentHeaderStart = headerStartIndex;
                    }
                }

                currentIndex = headerStartIndex + 1;
            }

            if (currentHeaderStart.HasValue)
            {
                if (SaveRifxFromMemoryMap(buffer, currentHeaderStart.Value, buffer.Length,
                                         blockOffset, filePath, innerCount, extractedDir, extractedFiles))
                {
                    extractedCount++;
                }
            }

            index = currentIndex;
            return extractedCount;
        }

        private int ProcessRiffMemoryMapBlock(byte[] buffer, long blockOffset, string filePath,
                                            string extractedDir, List<string> extractedFiles, ref int index, ref int waveCount)
        {
            int extractedCount = 0;
            int headerStart = IndexOf(buffer, RIFF_HEADER, index);
            if (headerStart == -1)
            {
                index = buffer.Length;
                return extractedCount;
            }

            bool hasValidFormat = false;
            string format = "";

            if (IsValidWaveHeader(buffer, headerStart))
            {
                format = IdentifyAudioFormat(buffer, headerStart);
                hasValidFormat = !string.IsNullOrEmpty(format);

                if (hasValidFormat)
                {
                    waveCount++;
                    int fileSizeValue = BitConverter.ToInt32(buffer, headerStart + 4);
                    int waveEnd = headerStart + 8 + fileSizeValue;
                    if (waveEnd > buffer.Length)
                        waveEnd = buffer.Length;

                    if (SaveRiffFromMemoryMap(buffer, headerStart, waveEnd, blockOffset,
                                            filePath, waveCount, extractedDir, extractedFiles, format))
                    {
                        extractedCount++;
                    }
                    index = waveEnd;
                }
                else
                {
                    index = headerStart + 4;
                }
            }
            else
            {
                if (headerStart + 22 < buffer.Length)
                {
                    if (buffer[headerStart + 8] == 0x58 && buffer[headerStart + 9] == 0x57 &&
                        buffer[headerStart + 10] == 0x4D && buffer[headerStart + 11] == 0x41)
                    {
                        bool matchesXwmaPattern = true;
                        for (int i = 0; i < 6; i++)
                        {
                            if (buffer[headerStart + 16 + i] != XWMA_PATTERN[i])
                            {
                                matchesXwmaPattern = false;
                                break;
                            }
                        }
                        if (matchesXwmaPattern)
                        {
                            format = "xwma";
                            hasValidFormat = true;

                            int fileSizeValue = BitConverter.ToInt32(buffer, headerStart + 4);
                            int waveEnd = headerStart + 8 + fileSizeValue;
                            if (waveEnd > buffer.Length)
                                waveEnd = buffer.Length;

                            waveCount++;
                            if (SaveRiffFromMemoryMap(buffer, headerStart, waveEnd, blockOffset,
                                                    filePath, waveCount, extractedDir, extractedFiles, format))
                            {
                                extractedCount++;
                            }
                            index = waveEnd;
                        }
                    }
                }

                if (!hasValidFormat)
                {
                    index = headerStart + 4;
                }
            }

            return extractedCount;
        }

        private bool SaveRifxFromMemoryMap(byte[] buffer, int start, int end, long blockOffset,
                                         string filePath, int count, string extractedDir, List<string> extractedFiles)
        {
            int length = end - start;
            if (length <= 0) return false;

            string format = "wem";
            byte[] waveData = new byte[length];
            Array.Copy(buffer, start, waveData, 0, length);

            return SaveExtractedFile(waveData, Path.GetFileNameWithoutExtension(filePath),
                                    extractedDir, format, count, extractedFiles, out _);
        }

        private bool SaveRiffFromMemoryMap(byte[] buffer, int start, int end, long blockOffset,
                                         string filePath, int count, string extractedDir,
                                         List<string> extractedFiles, string format)
        {
            int length = end - start;
            if (length <= 8) return false;

            byte[] waveData = new byte[length];
            Array.Copy(buffer, start, waveData, 0, length);

            return SaveExtractedFile(waveData, Path.GetFileNameWithoutExtension(filePath),
                                    extractedDir, format, count, extractedFiles, out _);
        }

        private int FindNextHeader(byte[] content, int startIndex)
        {
            int riffNext = IndexOf(content, RIFF_HEADER, startIndex);
            int rifxNext = IndexOf(content, RIFX_HEADER, startIndex);
            if (riffNext != -1 && (rifxNext == -1 || riffNext < rifxNext))
            {
                return riffNext;
            }
            return rifxNext;
        }

        private bool IsValidWaveHeader(byte[] content, int startIndex)
        {
            if (startIndex + 12 >= content.Length)
                return false;
            if (content[startIndex + 8] != 0x57 || content[startIndex + 9] != 0x41 ||
                content[startIndex + 10] != 0x56 || content[startIndex + 11] != 0x45)
                return false;
            int fileSize = BitConverter.ToInt32(content, startIndex + 4);
            if (fileSize <= 0 || startIndex + 8 + fileSize > content.Length)
                return false;
            return true;
        }

        private string IdentifyAudioFormat(byte[] content, int startIndex)
        {
            if (startIndex + 0x18 >= content.Length) return "wav";
            int formatStart = startIndex + 0x10;
            if (content[formatStart] == 0x20 && content[startIndex + 0x14] == 0x70 && content[startIndex + 0x15] == 0x02)
                return "at3";
            if (content[formatStart] == 0x34 && content[startIndex + 0x14] == 0xFE && content[startIndex + 0x15] == 0xFF)
                return "at9";
            if (content[formatStart] == 0x34 && content[startIndex + 0x14] == 0x66 && content[startIndex + 0x15] == 0x01)
                return "xma";
            if (content[formatStart] == 0x42 && content[startIndex + 0x14] == 0xFF && content[startIndex + 0x15] == 0xFF)
                return "wem";
            if (content[formatStart] == 0x10 && content[startIndex + 0x14] == 0x01 && content[startIndex + 0x15] == 0x00)
                return "wav";
            return "wav";
        }

        private bool ExtractWaveFileWithSize(byte[] content, int start, int end, string filePath, int waveCount,
                                            string extractedDir, List<string> extractedFiles, string format)
        {
            int length = end - start;
            if (length <= 8) return false;

            byte[] waveData = new byte[length];
            Array.Copy(content, start, waveData, 0, length);

            return SaveExtractedFile(waveData, Path.GetFileNameWithoutExtension(filePath),
                                    extractedDir, format, waveCount, extractedFiles, out _);
        }

        private bool ExtractRifxWaveFileWithSize(byte[] content, int start, int end, string filePath, int innerCount,
                                                string extractedDir, List<string> extractedFiles)
        {
            int length = end - start;
            if (length <= 0) return false;

            string format = "wem";
            byte[] waveData = new byte[length];
            Array.Copy(content, start, waveData, 0, length);

            return SaveExtractedFile(waveData, Path.GetFileNameWithoutExtension(filePath),
                                    extractedDir, format, innerCount, extractedFiles, out _);
        }
    }
}
