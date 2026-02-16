namespace super_toolbox
{
    public class Tm2_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] TM2_SIGNATURE = new byte[] { 0x54, 0x49, 0x4D, 0x32 };
        private const int MIN_TM2_SIZE = 0x30;
        private const int MAX_REASONABLE_SIZE = 100 * 1024 * 1024;
        private const int MAX_REASONABLE_DIMENSION = 4096;
        private int fileCounter = 0;
        
        private static readonly HashSet<byte> VALID_VERSIONS = new HashSet<byte> { 3, 4, 5, 6 };
        
        private static readonly HashSet<byte> VALID_FORMATS = new HashSet<byte> { 0, 1 };

        public override void Extract(string path)
        {
            ExtractAsync(path).Wait();
        }

        public override async Task ExtractAsync(string path, CancellationToken cancellationToken = default)
        {
            if (File.Exists(path))
            {
                await ProcessSingleFileAsync(path, cancellationToken);
            }
            else if (Directory.Exists(path))
            {
                await ProcessDirectoryAsync(path, cancellationToken);
            }
            else
            {
                ExtractionError?.Invoke(this, $"路径不存在:{path}");
                OnExtractionFailed($"路径不存在:{path}");
            }
        }

        private async Task ProcessDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
        {
            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = files.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath},共找到{files.Count}个文件");

            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            byte[] content = File.ReadAllBytes(filePath);

                            string sourceDir = Path.GetDirectoryName(filePath) ?? "";
                            string baseName = Path.GetFileNameWithoutExtension(filePath);
                            string outputDirectory = Path.Combine(sourceDir, baseName);

                            int extracted = ExtractTm2Files(content, filePath, outputDirectory, cancellationToken);
                            Interlocked.Add(ref totalExtractedFiles, extracted);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                        }
                    });
                }, cancellationToken);

                if (totalExtractedFiles > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成,共提取出{totalExtractedFiles}个TM2文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成,未找到TM2格式文件");
                }
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ProcessSingleFileAsync(string filePath, CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(filePath);
            string fileDir = Path.GetDirectoryName(filePath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string outputDirectory = Path.Combine(fileDir, baseName + "");

            Directory.CreateDirectory(outputDirectory);

            ExtractionStarted?.Invoke(this, $"开始处理单个文件:{fileName}");
            TotalFilesToExtract = 1;

            try
            {
                byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                int extracted = ExtractTm2Files(content, filePath, outputDirectory, cancellationToken);

                if (extracted > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成,从{fileName}中提取出{extracted}个TM2文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, $"处理完成,在{fileName}中未找到TM2格式文件");
                }
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
                OnExtractionFailed($"处理文件{fileName}时出错:{ex.Message}");
                throw;
            }
        }

        private int ExtractTm2Files(byte[] content, string sourceFilePath, string outputDirectory, CancellationToken cancellationToken)
        {
            int index = 0;
            string baseFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            int extractedCount = 0;

            Directory.CreateDirectory(outputDirectory);

            while (index <= content.Length - TM2_SIGNATURE.Length)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int startIndex = IndexOf(content, TM2_SIGNATURE, index);
                if (startIndex == -1) break;

                if (!IsValidTm2Header(content, startIndex))
                {
                    index = startIndex + 1;
                    continue;
                }

                int totalSize = ReadLittleEndianInt32(content, startIndex + 0x10);
                
                if (!IsValidTm2Size(totalSize, startIndex, content.Length))
                {
                    index = startIndex + 1;
                    continue;
                }

                int totalFileSize = totalSize + 16;

                byte imageCount = content[startIndex + 0x06];
                
                if (imageCount > 1)
                {
                    if (!ValidateMultiImageTm2(content, startIndex, totalFileSize))
                    {
                        index = startIndex + 1;
                        continue;
                    }
                }

                byte[] tm2Data = new byte[totalFileSize];
                Array.Copy(content, startIndex, tm2Data, 0, totalFileSize);

                fileCounter++;
                extractedCount++;

                string tm2FileName = $"{baseFileName}_{fileCounter}.tm2";
                string tm2FilePath = Path.Combine(outputDirectory, tm2FileName);
                tm2FilePath = MakeUniqueFilename(tm2FilePath);

                File.WriteAllBytes(tm2FilePath, tm2Data);
                OnFileExtracted(tm2FilePath);
                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(tm2FilePath)}");

                index = startIndex + totalFileSize;
            }

            return extractedCount;
        }

        private bool IsValidTm2Header(byte[] data, int offset)
        {
            if (offset + 0x30 > data.Length)
                return false;

            byte version = data[offset + 0x04];
            if (!VALID_VERSIONS.Contains(version))
                return false;

            byte format = data[offset + 0x05];
            if (!VALID_FORMATS.Contains(format))
                return false;

            byte imageCount = data[offset + 0x06];
            if (imageCount == 0)
                return false;

            short headerSize = ReadLittleEndianInt16(data, offset + 0x1C);
            if (headerSize < 0x30 || headerSize > 0x80)
                return false;

            short width = ReadLittleEndianInt16(data, offset + 0x24);
            short height = ReadLittleEndianInt16(data, offset + 0x26);
            
            if (width <= 0 || height <= 0 || 
                width > MAX_REASONABLE_DIMENSION || 
                height > MAX_REASONABLE_DIMENSION)
                return false;

            byte imageType = data[offset + 0x23];
            if (!IsValidImageType(imageType))
                return false;

            if (IsIndexedImage(imageType))
            {
                byte clutType = data[offset + 0x22];
                if (!IsValidClutType(clutType))
                    return false;

                short clutColorCount = ReadLittleEndianInt16(data, offset + 0x1E);
                if (!IsValidClutColorCount(clutColorCount, imageType))
                    return false;
            }

            return true;
        }

        private bool IsValidTm2Size(int totalSize, int offset, int fileLength)
        {
            if (totalSize <= 0 || totalSize > MAX_REASONABLE_SIZE)
                return false;

            int totalFileSize = totalSize + 16;
            
            if (offset + totalFileSize > fileLength)
                return false;

            return true;
        }

        private bool ValidateMultiImageTm2(byte[] data, int startOffset, int totalFileSize)
        {
            int currentOffset = startOffset;
            int endOffset = startOffset + totalFileSize;
            byte imageCount = data[startOffset + 0x06];

            for (int i = 0; i < imageCount; i++)
            {
                if (currentOffset + 0x30 > endOffset)
                    return false;

                if (!IsValidTm2Header(data, currentOffset))
                    return false;

                int subImageSize = ReadLittleEndianInt32(data, currentOffset + 0x10);
                if (subImageSize <= 0 || currentOffset + subImageSize + 16 > endOffset)
                    return false;

                currentOffset += subImageSize + 16;
            }

            return currentOffset == endOffset;
        }

        private bool IsValidImageType(byte imageType)
        {
            return imageType == 3 || imageType == 4 || imageType == 5 || imageType == 0;
        }

        private bool IsValidClutType(byte clutType)
        {
            return clutType == 1 || clutType == 2 || clutType == 3;
        }

        private bool IsIndexedImage(byte imageType)
        {
            return imageType == 4 || imageType == 5;
        }

        private bool IsValidClutColorCount(short count, byte imageType)
        {
            if (imageType == 4)
                return count == 16 || count == 256;
            else if (imageType == 5)
                return count == 256;
            else
                return count == 0;
        }

        private short ReadLittleEndianInt16(byte[] data, int offset)
        {
            if (offset + 2 > data.Length) return 0;
            return (short)(data[offset] | (data[offset + 1] << 8));
        }

        private int ReadLittleEndianInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            if (data == null || pattern == null || startIndex < 0 || startIndex > data.Length - pattern.Length)
                return -1;

            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
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

        private string MakeUniqueFilename(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;

            while (true)
            {
                string newPath = Path.Combine(directory, $"{fileName}_{counter}{extension}");
                if (!File.Exists(newPath))
                    return newPath;
                counter++;
            }
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
