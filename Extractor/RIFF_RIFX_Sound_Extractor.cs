using System.IO.MemoryMappedFiles;

namespace super_toolbox
{
    public class RIFF_RIFX_Sound_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] XWMA_PATTERN = { 0x12, 0x00, 0x00, 0x00, 0x61, 0x01 };
        private const long LARGE_FILE_THRESHOLD = 2L * 1024 * 1024 * 1024;
        private const long MEMORY_MAP_BUFFER_SIZE = 256 * 1024 * 1024;

        private int fileCounter = 0;
        private int staticCount = 0;     

        private async Task<int> ProcessLargeFileWithMemoryMapAsync(string filePath, string extractedDir,
                                                                  List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            long fileSize = new FileInfo(filePath).Length;

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

                        int localExtracted = ProcessBufferFast(buffer, position, filePath, extractedDir, extractedFiles);
                        extractedCount += localExtracted;
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

        private int ProcessBufferFast(byte[] buffer, long bufferOffset, string sourceFilePath,
                                     string outputDir, List<string> extractedFiles)
        {
            int extractedCount = 0;
            int position = 0;
            int waveCount = 0;

            while (position < buffer.Length - 8)
            {
                if (buffer[position] == 0x52 && buffer[position + 1] == 0x49 &&
                    buffer[position + 2] == 0x46)
                {
                    bool isRiff = buffer[position + 3] == 0x46;
                    bool isRifx = buffer[position + 3] == 0x58;

                    if (isRiff)
                    {
                        int result = ProcessRiffHeaderFast(buffer, position, bufferOffset,
                            sourceFilePath, outputDir, extractedFiles, ref waveCount);
                        if (result > 0)
                        {
                            extractedCount++;
                            position += result;
                            continue;
                        }
                    }
                    else if (isRifx)
                    {
                        int result = ProcessRifxHeaderFast(buffer, position, bufferOffset,
                            sourceFilePath, outputDir, extractedFiles);
                        if (result > 0)
                        {
                            extractedCount++;
                            position += result;
                            continue;
                        }
                    }
                }

                position++;
            }

            return extractedCount;
        }

        private int ProcessRiffHeaderFast(byte[] buffer, int position, long bufferOffset,
                                 string sourceFilePath, string outputDir,
                                 List<string> extractedFiles, ref int waveCount)
        {
            if (position + 12 >= buffer.Length)
                return 0;

            string format = "";

            if (buffer[position + 8] == 0x57 && buffer[position + 9] == 0x41 &&
                buffer[position + 10] == 0x56 && buffer[position + 11] == 0x45)
            {
                format = IdentifyFormatFast(buffer, position);
            }
            else if (buffer[position + 8] == 0x58 && buffer[position + 9] == 0x57 &&
                     buffer[position + 10] == 0x4D && buffer[position + 11] == 0x41)
            {
                if (position + 22 < buffer.Length)
                {
                    bool matchesXwmaPattern = true;
                    for (int i = 0; i < 6; i++)
                    {
                        if (buffer[position + 16 + i] != XWMA_PATTERN[i])
                        {
                            matchesXwmaPattern = false;
                            break;
                        }
                    }

                    if (matchesXwmaPattern)
                    {
                        format = "xwma";
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return 0;
                }
            }
            else
            {
                return 0;
            }

            int fileSize = BitConverter.ToInt32(buffer, position + 4);
            if (fileSize <= 0 || fileSize > buffer.Length - position - 8)
                return 0;

            int totalSize = fileSize + 8;
            int endPosition = position + totalSize;

            if (endPosition > buffer.Length)
                endPosition = buffer.Length;
            string fileName = $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_{waveCount + 1}.{format}";
            string filePath = Path.Combine(outputDir, fileName);

            SaveAudioFileFast(buffer, position, endPosition - position,
                filePath, extractedFiles);

            waveCount++;

            return totalSize;
        }

        private int ProcessRifxHeaderFast(byte[] buffer, int position, long bufferOffset,
                                         string sourceFilePath, string outputDir,
                                         List<string> extractedFiles)
        {
            if (position + 8 >= buffer.Length)
                return 0;

            int fileSize = (buffer[position + 7] << 24) |
                           (buffer[position + 6] << 16) |
                           (buffer[position + 5] << 8) |
                           buffer[position + 4];

            if (fileSize <= 0 || fileSize > buffer.Length - position)
                return 0;

            int endPosition = position + fileSize;
            if (endPosition > buffer.Length)
                endPosition = buffer.Length;

            if (endPosition - position > 16)
            {
                bool hasWaveFmt = true;
                byte[] waveFmtPattern = { 0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74 };
                for (int i = 0; i < 7; i++)
                {
                    if (buffer[position + 8 + i] != waveFmtPattern[i])
                    {
                        hasWaveFmt = false;
                        break;
                    }
                }

                if (!hasWaveFmt)
                    return 0;
            }

            staticCount++;
            string fileName = $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_{staticCount}.wem";
            string filePath = Path.Combine(outputDir, fileName);

            SaveAudioFileFast(buffer, position, endPosition - position,
                filePath, extractedFiles);

            return fileSize;
        }

        private void SaveAudioFileFast(byte[] data, int offset, int length,
                                      string filePath, List<string> extractedFiles)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? "");

                string uniqueFilePath = GenerateUniqueFilePath(filePath);

                using var fileStream = new FileStream(uniqueFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, false);
                fileStream.Write(data, offset, length);

                extractedFiles.Add(uniqueFilePath);
                fileCounter++;
                OnFileExtracted(uniqueFilePath);
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

            byte formatByte = buffer[position + 0x10];
            byte byte14 = buffer[position + 0x14];
            byte byte15 = buffer[position + 0x15];

            if (formatByte == 0x20 && byte14 == 0x70 && byte15 == 0x02)
                return "at3";
            if (formatByte == 0x34 && byte14 == 0xFE && byte15 == 0xFF)
                return "at9";
            if (formatByte == 0x34 && byte14 == 0x66 && byte15 == 0x01)
                return "xma";
            if (formatByte == 0x42 && byte14 == 0xFF && byte15 == 0xFF)
                return "wem";
            if (formatByte == 0x10 && byte14 == 0x01 && byte15 == 0x00)
                return "wav";

            return "wav";
        }

        private string GenerateUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;
            string newPath;

            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
                counter++;
            } while (File.Exists(newPath));

            return newPath;
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

            fileCounter = 0;
            staticCount = 0;

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
                        extractedCount = ProcessSmallFileFast(content, filePath, extractedDir, extractedFiles);
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

            ExtractionProgress?.Invoke(this, $"处理完成,共提取{fileCounter}个音频文件");
            OnExtractionCompleted();
        }

        private int ProcessSmallFileFast(byte[] content, string filePath,
                                        string extractedDir, List<string> extractedFiles)
        {
            return ProcessBufferFast(content, 0, filePath, extractedDir, extractedFiles);
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }  
}
