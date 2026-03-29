using System.IO.MemoryMappedFiles;

namespace super_toolbox
{
    public class RIFF_RIFX_Sound_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private static readonly byte[] XWMA_PATTERN = { 0x12, 0x00, 0x00, 0x00, 0x61, 0x01 };
        private const long LARGE_FILE_THRESHOLD = 2L * 1024 * 1024 * 1024;
        private const long MEMORY_MAP_BUFFER_SIZE = 256 * 1024 * 1024;

        private int fileCounter = 0;

        public async Task<int> ProcessFileAsync(string filePath, string outputDir, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            int count = 0;

            FileInfo fi = new FileInfo(filePath);
            if (fi.Length < LARGE_FILE_THRESHOLD)
            {
                byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                count = ProcessBufferFast(content, 0, filePath, outputDir, extractedFiles);
            }
            else
            {
                count = await ProcessLargeFileWithMemoryMapAsync(filePath, outputDir, extractedFiles, cancellationToken);
            }

            return count;
        }

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
            string? format = null;
            if (position + 22 < buffer.Length)
            {
                if (buffer[position + 16] == 0x08 && buffer[position + 17] == 0x00 &&
                    buffer[position + 18] == 0x00 && buffer[position + 19] == 0x00 &&
                    buffer[position + 20] == 0x64 && buffer[position + 21] == 0x00)
                {
                    format = "bank";
                }
            }
            if (format == null)
            {
                if (buffer[position + 8] == 0x57 && buffer[position + 9] == 0x41 && buffer[position + 10] == 0x56 && buffer[position + 11] == 0x45)
                {
                    format = IdentifyFormatFast(buffer, position);
                    if (format == null)
                        return 0;
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
            }

            int chunkSize = BitConverter.ToInt32(buffer, position + 4);
            if (chunkSize <= 0)
                return 0;

            int totalSize = chunkSize + 8;
            if (position + totalSize > buffer.Length)
                return 0;

            string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string fileName = extractedFiles.Count == 0
                ? $"{baseName}.{format}"
                : $"{baseName}_{audioIndex + 1}.{format}";

            string filePath = Path.Combine(outputDir, fileName);

            SaveAudioFileFast(buffer, position, totalSize, filePath, extractedFiles);
            return totalSize;
        }

        private int ProcessRifxHeaderFast(byte[] buffer, int position, long bufferOffset, string sourceFilePath, string outputDir, List<string> extractedFiles, int audioIndex)
        {
            if (position + 16 > buffer.Length)
                return 0;

            Span<byte> magic = new Span<byte>(buffer, position + 0x10, 6);
            int totalSize = 0;

            if (magic.SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x20, 0x01, 0x65 }))
            {
                totalSize = (buffer[position + 7] << 24) | (buffer[position + 6] << 16) |
                            (buffer[position + 5] << 8) | buffer[position + 4];
            }
            else if (magic.SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x40, 0x01, 0x66 }))
            {
                totalSize = (buffer[position + 7] << 24) | (buffer[position + 6] << 16) |
                            (buffer[position + 5] << 8) | buffer[position + 4];
            }
            else if (magic.SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x28, 0xFF, 0xFF }))
            {
                totalSize = ((buffer[position + 4] << 24) | (buffer[position + 5] << 16) |
                            (buffer[position + 6] << 8) | buffer[position + 7]) + 8;
            }
            else
            {
                return 0;
            }

            if (totalSize <= 0)
                return 0;

            if (position + totalSize > buffer.Length)
                return 0;

            string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string fileName = extractedFiles.Count == 0
                ? $"{baseName}.wem"
                : $"{baseName}_{audioIndex + 1}.wem";

            string filePath = Path.Combine(outputDir, fileName);

            SaveAudioFileFast(buffer, position, totalSize, filePath, extractedFiles);
            return totalSize;
        }

        private void SaveAudioFileFast(byte[] data, int offset, int length, string filePath, List<string> extractedFiles)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    ExtractionError?.Invoke(this, "保存文件时路径为空");
                    return;
                }

                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

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

        private string? IdentifyFormatFast(byte[] buffer, int position)
        {
            if (buffer.Length - position < 0x18)
                return null;

            Span<byte> magic = new Span<byte>(buffer, position + 0x10, 6);

            var formatPatterns = new Dictionary<string, List<byte[]>>
            {
                ["bank"] = new List<byte[]>
                {
                new byte[] { 0x08, 0x00, 0x00, 0x00, 0x64, 0x00 }
                },
                ["at3"] = new List<byte[]>
                {
                new byte[] { 0x20, 0x00, 0x00, 0x00, 0x70, 0x02 }
                },
                ["at9"] = new List<byte[]>
                {
                new byte[] { 0x34, 0x00, 0x00, 0x00, 0xFE, 0xFF }
                },
                ["xma"] = new List<byte[]>
                {
                new byte[] { 0x34, 0x00, 0x00, 0x00, 0x66, 0x01 },
                new byte[] { 0x14, 0x00, 0x00, 0x00, 0x69, 0x00 },
                new byte[] { 0x20, 0x00, 0x00, 0x00, 0x65, 0x01 },
                new byte[] { 0x00, 0x90, 0x01, 0x00, 0x2C, 0x00 },
                new byte[] { 0x32, 0x00, 0x00, 0x00, 0x02, 0x00 }
                },
                ["pcm"] = new List<byte[]>
                {
                new byte[] { 0x14, 0x00, 0x00, 0x00, 0x11, 0x00 }
                },
                ["wem"] = new List<byte[]>
                {
                new byte[] { 0x42, 0x00, 0x00, 0x00, 0xFF, 0xFF },
                new byte[] { 0x18, 0x00, 0x00, 0x00, 0x02, 0x00 },
                new byte[] { 0x18, 0x00, 0x00, 0x00, 0x11, 0x83 },
                new byte[] { 0x18, 0x00, 0x00, 0x00, 0xFE, 0xFF },
                new byte[] { 0x28, 0x00, 0x00, 0x00, 0x39, 0x30 },
                new byte[] { 0x10, 0x00, 0x00, 0x00, 0xFE, 0xFF }
                },
                ["wav"] = new List<byte[]>
                {
                new byte[] { 0x10, 0x00, 0x00, 0x00, 0x01, 0x00 }
                }
            };

            foreach (var format in formatPatterns)
            {
                foreach (var pattern in format.Value)
                {
                    if (magic.SequenceEqual(pattern))
                        return format.Key;
                }
            }

            return null;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

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
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fp);
                    string fileDirectory = Path.GetDirectoryName(fp) ?? directoryPath;
                    string outputDir = Path.Combine(fileDirectory, fileNameWithoutExt);

                    int extractCount = 0;
                    FileInfo fi = new FileInfo(fp);

                    if (fi.Length < LARGE_FILE_THRESHOLD)
                    {
                        byte[] content = await File.ReadAllBytesAsync(fp, cancellationToken);
                        extractCount = ProcessBufferFast(content, 0, fp, outputDir, extractedFiles);
                    }
                    else
                    {
                        extractCount = await ProcessLargeFileWithMemoryMapAsync(fp, outputDir, extractedFiles, cancellationToken);
                    }

                    total += extractCount;
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
