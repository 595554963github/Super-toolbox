namespace super_toolbox
{
    public class HcaExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] START_SEQ_1 = { 0x48, 0x43, 0x41, 0x00 };
        private static readonly byte[] START_SEQ_2 = { 0xC8, 0xC3, 0xC1, 0x00, 0x03, 0x00, 0x00, 0x60 };
        private static readonly byte[] HCA_BLOCK_MARKER = { 0x66, 0x6D, 0x74 };

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

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .ToList();

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
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);

                        int count1 = await ExtractHcaTypeAsync(content, filePath, START_SEQ_1, HCA_BLOCK_MARKER, extractedFiles, cancellationToken, false);
                        int count2 = await ExtractHcaTypeAsync(content, filePath, START_SEQ_2, null, extractedFiles, cancellationToken, true);
                        int count = count1 + count2;

                        if (count > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中提取出{count}个HCA文件");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个HCA文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成,未找到HCA文件");
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

        private async Task<int> ExtractHcaTypeAsync(byte[] content, string filePath, byte[] startSequence, byte[]? blockMarker, List<string> extractedFiles, CancellationToken cancellationToken, bool isEncrypted)
        {
            int count = 0;
            int index = 0;
            List<int> headerPositions = new List<int>();

            while (index < content.Length)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int headerStartIndex = IndexOf(content, startSequence, index);
                if (headerStartIndex == -1)
                {
                    break;
                }

                headerPositions.Add(headerStartIndex);
                index = headerStartIndex + 1;
            }

            if (headerPositions.Count == 0)
                return 0;

            string fileDirectory = Path.GetDirectoryName(filePath) ?? "";
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputDir;

            if (headerPositions.Count == 1)
            {
                outputDir = fileDirectory;
            }
            else
            {
                outputDir = Path.Combine(fileDirectory, baseFileName);
                Directory.CreateDirectory(outputDir);
            }

            for (int i = 0; i < headerPositions.Count; i++)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int headerStartIndex = headerPositions[i];
                int nextHeaderIndex = (i + 1 < headerPositions.Count) ? headerPositions[i + 1] : -1;
                int endIndex = nextHeaderIndex == -1 ? content.Length : nextHeaderIndex;

                byte[] extractedData = new byte[endIndex - headerStartIndex];
                Array.Copy(content, headerStartIndex, extractedData, 0, extractedData.Length);

                if (blockMarker == null || ContainsBytes(extractedData, blockMarker))
                {
                    count++;

                    string outputFileName;
                    if (isEncrypted)
                    {
                        outputFileName = headerPositions.Count == 1
                            ? $"{baseFileName}_enc.hca"
                            : $"{baseFileName}_{count}_enc.hca";
                    }
                    else
                    {
                        outputFileName = headerPositions.Count == 1
                            ? $"{baseFileName}.hca"
                            : $"{baseFileName}_{count}.hca";
                    }

                    string outputFilePath = Path.Combine(outputDir, outputFileName);
                    outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                    try
                    {
                        await File.WriteAllBytesAsync(outputFilePath, extractedData, cancellationToken);
                        extractedFiles.Add(outputFilePath);
                        OnFileExtracted(outputFilePath);
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)}");
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{ex.Message}");
                        OnExtractionFailed($"写入文件{outputFilePath}时出错:{ex.Message}");
                    }
                }
            }

            return count;
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
            }
            while (File.Exists(newPath));

            return newPath;
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

        private static bool ContainsBytes(byte[] data, byte[] pattern)
        {
            return IndexOf(data, pattern, 0) != -1;
        }
    }
}
