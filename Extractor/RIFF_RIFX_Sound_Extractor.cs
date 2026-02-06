using System.IO.MemoryMappedFiles;

namespace super_toolbox
{
    public class RIFF_RIFX_Sound_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] XWMA_PATTERN = { 0x12, 0x00, 0x00, 0x00, 0x61, 0x01 };
        private static readonly byte[] RIFX_VALID_PATTERN_1 = { 0x00, 0x00, 0x00, 0x20, 0x01, 0x65, 0x00, 0x10 };
        private static readonly byte[] RIFX_VALID_PATTERN_2 = { 0x00, 0x00, 0x00, 0x40, 0x01, 0x66, 0x00, 0x01 };
        private const long LARGE_FILE_THRESHOLD = 2L * 1024 * 1024 * 1024;
        private const long MEMORY_MAP_BUFFER_SIZE = 256 * 1024 * 1024;

        private int fileCounter = 0;

        private async Task<int> ProcessLargeFileWithMemoryMapAsync(string filePath, string extractedDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            long fileSize = new FileInfo(filePath).Length;
            int audioIndex = 0;

            using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
            {
                long position = 0;

                while (position < fileSize - 8)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    long bufferSize = Math.Min(MEMORY_MAP_BUFFER_SIZE, fileSize - position);
                    if (bufferSize < 8) break;

                    using (var accessor = mmf.CreateViewAccessor(position, bufferSize, MemoryMappedFileAccess.Read))
                    {
                        byte[] buffer = new byte[bufferSize];
                        accessor.ReadArray(0, buffer, 0, (int)bufferSize);

                        int localPos = 0;
                        while (localPos < buffer.Length - 8)
                        {
                            if (buffer[localPos] == 0x52 && buffer[localPos + 1] == 0x49 && buffer[localPos + 2] == 0x46)
                            {
                                bool isRiff = buffer[localPos + 3] == 0x46;
                                bool isRifx = buffer[localPos + 3] == 0x58;
                                int step = 0;

                                if (isRiff)
                                {
                                    step = ProcessRiffHeaderFast(buffer, localPos, position, filePath, extractedDir, extractedFiles, audioIndex);
                                }
                                else if (isRifx)
                                {
                                    step = ProcessRifxHeaderFast(buffer, localPos, position, filePath, extractedDir, extractedFiles, audioIndex);
                                }

                                if (step > 0)
                                {
                                    extractedCount++;
                                    audioIndex++;
                                    localPos += step;
                                    continue;
                                }
                            }
                            localPos++;
                        }
                    }

                    position += bufferSize;

                    if (fileSize > 0)
                    {
                        int progressPercentage = (int)(position * 100 / fileSize);
                        ExtractionProgress?.Invoke(this, $"处理大文件:{Path.GetFileName(filePath)} - 进度{progressPercentage}%");
                    }
                }
            }
            return extractedCount;
        }

        private int ProcessBufferFast(byte[] buffer, long bufferOffset, string sourceFilePath, string outputDir, List<string> extractedFiles)
        {
            int extractedCount = 0;
            int position = 0;
            int audioIndex = 0;

            while (position < buffer.Length - 8)
            {
                if (buffer[position] == 0x52 && buffer[position + 1] == 0x49 && buffer[position + 2] == 0x46)
                {
                    bool isRiff = buffer[position + 3] == 0x46;
                    bool isRifx = buffer[position + 3] == 0x58;
                    int step = 0;

                    if (isRiff)
                    {
                        step = ProcessRiffHeaderFast(buffer, position, bufferOffset, sourceFilePath, outputDir, extractedFiles, audioIndex);
                    }
                    else if (isRifx)
                    {
                        step = ProcessRifxHeaderFast(buffer, position, bufferOffset, sourceFilePath, outputDir, extractedFiles, audioIndex);
                    }

                    if (step > 0)
                    {
                        extractedCount++;
                        audioIndex++;
                        position += step;
                        continue;
                    }
                }
                position++;
            }
            return extractedCount;
        }

        private int ProcessRiffHeaderFast(byte[] buffer, int position, long bufferOffset, string sourceFilePath, string outputDir, List<string> extractedFiles, int audioIndex)
        {
            if (position + 12 >= buffer.Length)
                return 0;

            string format = "";

            if (buffer[position + 8] == 0x57 && buffer[position + 9] == 0x41 && buffer[position + 10] == 0x56 && buffer[position + 11] == 0x45)
            {
                format = IdentifyFormatFast(buffer, position);
            }
            else if (buffer[position + 8] == 0x58 && buffer[position + 9] == 0x57 && buffer[position + 10] == 0x4D && buffer[position + 11] == 0x41)
            {
                if (position + 22 >= buffer.Length)
                    return 0;

                bool matchesXwmaPattern = true;
                for (int i = 0; i < 6; i++)
                {
                    if (buffer[position + 16 + i] != XWMA_PATTERN[i])
                    {
                        matchesXwmaPattern = false;
                        break;
                    }
                }
                if (!matchesXwmaPattern)
                    return 0;

                format = "xwma";
            }
            else
            {
                return 0;
            }

            int chunkSize = BitConverter.ToInt32(buffer, position + 4);
            if (chunkSize <= 0)
                return 0;

            int totalSize = chunkSize + 8;
            if (position + totalSize > buffer.Length)
                return 0;

            string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string fileName;

            if (audioIndex == 0)
            {
                fileName = $"{baseName}.{format}";
            }
            else
            {
                fileName = $"{baseName}_{audioIndex + 1}.{format}";
            }

            string filePath = Path.Combine(outputDir, fileName);

            SaveAudioFileFast(buffer, position, totalSize, filePath, extractedFiles);
            return totalSize;
        }

        private int ProcessRifxHeaderFast(byte[] buffer, int position, long bufferOffset, string sourceFilePath, string outputDir, List<string> extractedFiles, int audioIndex)
        {
            if (position + 16 > buffer.Length)
                return 0;

            int totalSize = 0;
            bool isPattern1 = true;
            bool isPattern2 = true;
            bool isPattern3 = true;

            for (int i = 0; i < RIFX_VALID_PATTERN_1.Length; i++)
            {
                if (buffer[position + 0x10 + i] != RIFX_VALID_PATTERN_1[i])
                {
                    isPattern1 = false;
                    break;
                }
            }

            for (int i = 0; i < RIFX_VALID_PATTERN_2.Length; i++)
            {
                if (buffer[position + 0x10 + i] != RIFX_VALID_PATTERN_2[i])
                {
                    isPattern2 = false;
                    break;
                }
            }

            if (buffer[position + 0x10] != 0x00 || buffer[position + 0x11] != 0x00 ||
                buffer[position + 0x12] != 0x00 || buffer[position + 0x13] != 0x28 ||
                buffer[position + 0x14] != 0xFF || buffer[position + 0x15] != 0xFF ||
                buffer[position + 0x16] != 0x00 || buffer[position + 0x17] != 0x01)
            {
                isPattern3 = false;
            }

            if (!isPattern1 && !isPattern2 && !isPattern3)
                return 0;

            if (isPattern3)
            {
                totalSize =
                    (buffer[position + 4] << 24) |
                    (buffer[position + 5] << 16) |
                    (buffer[position + 6] << 8) |
                    buffer[position + 7];
                totalSize += 8;
            }
            else
            {
                totalSize =
                    (buffer[position + 7] << 24) |
                    (buffer[position + 6] << 16) |
                    (buffer[position + 5] << 8) |
                    buffer[position + 4];
            }

            if (totalSize <= 0)
                return 0;

            if (position + totalSize > buffer.Length)
                return 0;

            string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string fileName;

            if (audioIndex == 0)
            {
                fileName = $"{baseName}.wem";
            }
            else
            {
                fileName = $"{baseName}_{audioIndex + 1}.wem";
            }

            string filePath = Path.Combine(outputDir, fileName);

            SaveAudioFileFast(buffer, position, totalSize, filePath, extractedFiles);
            return totalSize;
        }

        private void SaveAudioFileFast(byte[] data, int offset, int length, string filePath, List<string> extractedFiles)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? "");
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, false);
                fs.Write(data, offset, length);
                extractedFiles.Add(filePath);
                fileCounter++;
                OnFileExtracted(filePath);
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"写入文件{filePath}时出错:{ex.Message}");
            }
        }

        private string IdentifyFormatFast(byte[] buffer, int position)
        {
            if (buffer.Length - position < 0x18)
                return "wav";

            if (buffer[position + 0x10] == 0x18 && buffer[position + 0x11] == 0x00 &&
                buffer[position + 0x12] == 0x00 && buffer[position + 0x13] == 0x00 &&
                buffer[position + 0x14] == 0x02 && buffer[position + 0x15] == 0x00)
            {
                return "wem";
            }
            else if (buffer[position + 0x10] == 0x10 && buffer[position + 0x11] == 0x00 &&
                     buffer[position + 0x12] == 0x00 && buffer[position + 0x13] == 0x00 &&
                     buffer[position + 0x14] == 0x01 && buffer[position + 0x15] == 0x00 &&
                     buffer[position + 0x16] == 0x02 && buffer[position + 0x17] == 0x00)
            {
                return "wav";
            }

            byte f = buffer[position + 0x10];
            byte b14 = buffer[position + 0x14];
            byte b15 = buffer[position + 0x15];

            if (f == 0x20 && b14 == 0x70 && b15 == 0x02) return "at3";
            if (f == 0x34 && b14 == 0xFE && b15 == 0xFF) return "at9";
            if (f == 0x34 && b14 == 0x66 && b15 == 0x01) return "xma";
            if (f == 0x42 && b14 == 0xFF && b15 == 0xFF) return "wem";
            if (f == 0x10 && b14 == 0x01 && b15 == 0x00) return "wav";

            return "wav";
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
                .Where(f => !f.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            int processed = 0;
            int total = 0;

            fileCounter = 0;

            foreach (var fp in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                processed++;
                ExtractionProgress?.Invoke(this, $"正在处理源文件({processed}/{TotalFilesToExtract}):{Path.GetFileName(fp)}");

                try
                {
                    FileInfo fi = new FileInfo(fp);
                    int cnt = 0;

                    if (fi.Length < LARGE_FILE_THRESHOLD)
                    {
                        byte[] content = await File.ReadAllBytesAsync(fp, cancellationToken);
                        cnt = ProcessBufferFast(content, 0, fp, extractedDir, extractedFiles);
                    }
                    else
                    {
                        cnt = await ProcessLargeFileWithMemoryMapAsync(fp, extractedDir, extractedFiles, cancellationToken);
                    }
                    total += cnt;
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (IOException e)
                {
                    ExtractionError?.Invoke(this, $"读取文件{fp}时出错:{e.Message}");
                    OnExtractionFailed($"读取文件{fp}时出错:{e.Message}");
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{fp}时发生错误:{e.Message}");
                    OnExtractionFailed($"处理文件{fp}时发生错误:{e.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"处理完成,共提取{fileCounter}个音频文件");
            OnExtractionCompleted();
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
