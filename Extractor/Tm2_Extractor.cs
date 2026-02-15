namespace super_toolbox
{
    public class Tm2_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] TM2_SIGNATURE = new byte[] { 0x54, 0x49, 0x4D, 0x32 };
        private int fileCounter = 0;

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

                int sizeOffset = startIndex + 0x10;
                if (sizeOffset + 4 > content.Length)
                {
                    index = startIndex + 1;
                    continue;
                }

                int sizeFromOffset = ReadLittleEndianInt32(content, sizeOffset);
                int totalFileSize = sizeFromOffset + 16;

                if (totalFileSize <= 0 || startIndex + totalFileSize > content.Length || totalFileSize > 100 * 1024 * 1024)
                {
                    index = startIndex + 1;
                    continue;
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