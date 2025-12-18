using System.IO.MemoryMappedFiles;

namespace super_toolbox
{
    public class DarExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const long LARGE_FILE_THRESHOLD = 2L * 1024 * 1024 * 1024;
        private const long MEMORY_MAP_BUFFER_SIZE = 64 * 1024 * 1024;
        private static readonly byte[] RIFF_HEADER = new byte[] { 0x52, 0x49, 0x46, 0x46 };

        private int fileCounter = 0;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        string fileName = Path.GetFileName(filePath).ToLower();

                        if (fileName == "data.dar")
                        {
                            await ProcessDataDarFileAsync(filePath, cancellationToken);
                        }
                        else if (fileName == "voice.dar")
                        {
                            await ProcessVoiceDarFileAsync(filePath, cancellationToken);
                        }
                        else
                        {
                            await ProcessNormalFileAsync(filePath, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,共提取{fileCounter}个音频文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ProcessDataDarFileAsync(string darFilePath, CancellationToken cancellationToken)
        {
            ExtractionProgress?.Invoke(this, $"处理data.dar文件: {Path.GetFileName(darFilePath)}");

            string baseName = Path.GetFileNameWithoutExtension(darFilePath);
            string outputDir = Path.Combine(Path.GetDirectoryName(darFilePath) ?? "", baseName);
            Directory.CreateDirectory(outputDir);

            int elzmaCount = 0;
            int audioCount = 0;

            try
            {
                using (var fs = new FileStream(darFilePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    uint nullValue = reader.ReadUInt32();
                    uint part = reader.ReadUInt32();
                    uint files = reader.ReadUInt32();

                    fs.Seek(0x10, SeekOrigin.Begin);

                    ExtractionProgress?.Invoke(this, $"data.dar中共有{files}个文件");

                    for (int i = 0; i < files; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        uint size = reader.ReadUInt32();
                        uint zsize = reader.ReadUInt32();
                        ulong offset = reader.ReadUInt64();
                        byte[] null3 = reader.ReadBytes(0x10);

                        if (zsize == 0)
                        {
                            continue;
                        }
                        else
                        {
                            elzmaCount++;
                            string outputFile = Path.Combine(outputDir, $"{baseName}_{elzmaCount}.elzma");

                            long currentPos = fs.Position;
                            fs.Seek((long)offset, SeekOrigin.Begin);
                            byte[] fileData = reader.ReadBytes((int)zsize);
                            fs.Seek(currentPos, SeekOrigin.Begin);

                            await File.WriteAllBytesAsync(outputFile, fileData, cancellationToken);
                        }

                        if (i % 100 == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"提取进度:{i + 1}/{files}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"elzma文件提取完成,共{elzmaCount}个");

                    fs.Seek(0, SeekOrigin.Begin);

                    audioCount = await ExtractAudioFromDataDarAsync(fs, outputDir, baseName, cancellationToken);
                }

                ExtractionProgress?.Invoke(this, $"data.dar处理完成:{elzmaCount}个elzma文件,{audioCount}个音频文件");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理data.dar时出错:{ex.Message}");
            }
        }

        private async Task<int> ExtractAudioFromDataDarAsync(FileStream fs, string outputDir, string baseName, CancellationToken cancellationToken)
        {
            fs.Seek(0, SeekOrigin.Begin);
            byte[] content = new byte[fs.Length];
            await fs.ReadAsync(content, 0, content.Length, cancellationToken);

            int position = 0;
            int audioCount = 0;

            while (position < content.Length - 8)
            {
                ThrowIfCancellationRequested(cancellationToken);

                if (content[position] == RIFF_HEADER[0] &&
                    content[position + 1] == RIFF_HEADER[1] &&
                    content[position + 2] == RIFF_HEADER[2] &&
                    content[position + 3] == RIFF_HEADER[3])
                {
                    if (position + 8 <= content.Length)
                    {
                        int fileSize = BitConverter.ToInt32(content, position + 4);
                        long totalBlockSize = fileSize + 8;
                        if (position + totalBlockSize <= content.Length)
                        {
                            audioCount++;
                            string outputFileName = $"{baseName}_{audioCount}.at9";
                            string outputFilePath = Path.Combine(outputDir, outputFileName);
                            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                            byte[] fileData = new byte[totalBlockSize];
                            Array.Copy(content, position, fileData, 0, (int)totalBlockSize);

                            await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);

                            fileCounter++;
                            OnFileExtracted(outputFilePath);
                            ExtractionProgress?.Invoke(this, $"已提取音频文件:{outputFileName}");

                            position += (int)totalBlockSize;
                            continue;
                        }
                    }
                }

                position++;
            }

            return audioCount;
        }

        private async Task ProcessVoiceDarFileAsync(string darFilePath, CancellationToken cancellationToken)
        {
            string baseName = Path.GetFileNameWithoutExtension(darFilePath);
            string outputDir = Path.Combine(Path.GetDirectoryName(darFilePath) ?? "", baseName);
            Directory.CreateDirectory(outputDir);

            FileInfo fileInfo = new FileInfo(darFilePath);
            if (fileInfo.Length < LARGE_FILE_THRESHOLD)
            {
                byte[] content = await File.ReadAllBytesAsync(darFilePath, cancellationToken);
                await ExtractAudioFromVoiceDarAsync(content, darFilePath, outputDir, baseName, cancellationToken);
            }
            else
            {
                await ExtractLargeFileAsync(darFilePath, outputDir, new List<string>(), cancellationToken);
            }
        }

        private async Task ExtractAudioFromVoiceDarAsync(byte[] content, string filePath, string outputDir, string baseName, CancellationToken cancellationToken)
        {
            int position = 0;
            int audioCount = 0;

            while (position < content.Length - 8)
            {
                ThrowIfCancellationRequested(cancellationToken);

                if (content[position] == RIFF_HEADER[0] &&
                    content[position + 1] == RIFF_HEADER[1] &&
                    content[position + 2] == RIFF_HEADER[2] &&
                    content[position + 3] == RIFF_HEADER[3])
                {
                    if (position + 8 <= content.Length)
                    {
                        int fileSize = BitConverter.ToInt32(content, position + 4);
                        long totalBlockSize = fileSize + 8;
                        if (position + totalBlockSize <= content.Length)
                        {
                            audioCount++;
                            string outputFileName = $"{baseName}_{audioCount}.at9";
                            string outputFilePath = Path.Combine(outputDir, outputFileName);
                            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                            byte[] fileData = new byte[totalBlockSize];
                            Array.Copy(content, position, fileData, 0, (int)totalBlockSize);

                            await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);

                            fileCounter++;
                            OnFileExtracted(outputFilePath);
                            ExtractionProgress?.Invoke(this, $"已提取音频文件:{outputFileName}");

                            position += (int)totalBlockSize;
                            continue;
                        }
                    }
                }

                position++;
            }
        }

        private async Task ProcessNormalFileAsync(string filePath, CancellationToken cancellationToken)
        {
            string sourceFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputDir = Path.Combine(Path.GetDirectoryName(filePath) ?? "", sourceFileName);
            Directory.CreateDirectory(outputDir);

            FileInfo fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < LARGE_FILE_THRESHOLD)
            {
                byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                await ExtractAt9AudioFilesAsync(content, filePath, outputDir, sourceFileName, cancellationToken);
            }
            else
            {
                await ExtractLargeFileAsync(filePath, outputDir, new List<string>(), cancellationToken);
            }
        }

        private async Task ExtractAt9AudioFilesAsync(byte[] content, string filePath,
            string outputDir, string baseName, CancellationToken cancellationToken)
        {
            int position = 0;
            int fileNumber = 0;

            while (position < content.Length - 8)
            {
                ThrowIfCancellationRequested(cancellationToken);

                if (content[position] == RIFF_HEADER[0] &&
                    content[position + 1] == RIFF_HEADER[1] &&
                    content[position + 2] == RIFF_HEADER[2] &&
                    content[position + 3] == RIFF_HEADER[3])
                {
                    if (position + 8 <= content.Length)
                    {
                        int fileSize = BitConverter.ToInt32(content, position + 4);
                        long totalBlockSize = fileSize + 8;
                        if (position + totalBlockSize <= content.Length)
                        {
                            fileNumber++;
                            string outputFileName = $"{baseName}_{fileNumber}.at9";
                            string outputFilePath = Path.Combine(outputDir, outputFileName);
                            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                            byte[] fileData = new byte[totalBlockSize];
                            Array.Copy(content, position, fileData, 0, (int)totalBlockSize);

                            await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);

                            fileCounter++;
                            OnFileExtracted(outputFilePath);
                            ExtractionProgress?.Invoke(this, $"已提取音频文件:{outputFileName}");

                            position += (int)totalBlockSize;
                            continue;
                        }
                    }
                }

                position++;
            }
        }

        private async Task ExtractLargeFileAsync(string filePath, string outputDir,
                                               List<string> extractedFiles, CancellationToken cancellationToken)
        {
            long fileSize = new FileInfo(filePath).Length;
            int fileNumber = 0;
            string baseName = Path.GetFileNameWithoutExtension(filePath);

            using (var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
            {
                long position = 0;

                while (position < fileSize - 8)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var headerAccessor = mmf.CreateViewAccessor(position, 4, MemoryMappedFileAccess.Read))
                    {
                        byte[] headerBuffer = new byte[4];
                        headerAccessor.ReadArray(0, headerBuffer, 0, 4);

                        if (headerBuffer[0] == RIFF_HEADER[0] &&
                            headerBuffer[1] == RIFF_HEADER[1] &&
                            headerBuffer[2] == RIFF_HEADER[2] &&
                            headerBuffer[3] == RIFF_HEADER[3])
                        {
                            if (position + 8 <= fileSize)
                            {
                                using (var sizeAccessor = mmf.CreateViewAccessor(position + 4, 4, MemoryMappedFileAccess.Read))
                                {
                                    byte[] sizeBuffer = new byte[4];
                                    sizeAccessor.ReadArray(0, sizeBuffer, 0, 4);
                                    int riffFileSize = BitConverter.ToInt32(sizeBuffer, 0);

                                    long totalBlockSize = riffFileSize + 8;

                                    if (position + totalBlockSize <= fileSize)
                                    {
                                        fileNumber++;
                                        await ExtractAudioFromMemoryMapAsync(mmf, position, totalBlockSize, filePath,
                                            outputDir, extractedFiles, fileNumber, baseName, cancellationToken);

                                        position += totalBlockSize;
                                        continue;
                                    }
                                }
                            }
                        }
                    }

                    position++;
                    if (position % (100 * 1024 * 1024) == 0)
                    {
                        ExtractionProgress?.Invoke(this, $"扫描进度:{position / (1024 * 1024)}MB/{fileSize / (1024 * 1024)}MB");
                    }
                }
            }
        }

        private async Task ExtractAudioFromMemoryMapAsync(MemoryMappedFile mmf, long startOffset, long size,
                                                       string sourceFilePath, string outputDir,
                                                       List<string> extractedFiles, int fileNumber, string baseName,
                                                       CancellationToken cancellationToken)
        {
            if (size <= 0)
                return;

            string outputFileName = $"{baseName}_{fileNumber}.at9";
            string outputFilePath = Path.Combine(outputDir, outputFileName);
            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

            try
            {
                using (var outputStream = File.OpenWrite(outputFilePath))
                {
                    long remaining = size;
                    long currentOffset = startOffset;

                    while (remaining > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long chunkSize = Math.Min(MEMORY_MAP_BUFFER_SIZE, remaining);

                        using (var accessor = mmf.CreateViewAccessor(currentOffset, chunkSize, MemoryMappedFileAccess.Read))
                        {
                            byte[] buffer = new byte[chunkSize];
                            accessor.ReadArray(0, buffer, 0, (int)chunkSize);
                            await outputStream.WriteAsync(buffer, 0, (int)chunkSize, cancellationToken);
                        }

                        currentOffset += chunkSize;
                        remaining -= chunkSize;
                    }
                }

                extractedFiles.Add(outputFilePath);
                fileCounter++;
                OnFileExtracted(outputFilePath);

                ExtractionProgress?.Invoke(this, $"已提取音频文件:{outputFileName}");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{ex.Message}");
            }
        }

        private async Task<string> GenerateUniqueFilePathAsync(string filePath, CancellationToken cancellationToken)
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
                ThrowIfCancellationRequested(cancellationToken);
            } while (File.Exists(newPath));

            return newPath;
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
