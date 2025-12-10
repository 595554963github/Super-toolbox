namespace super_toolbox
{
    public class XenobladeBC_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] BC_START_SIGNATURE = { 0x42, 0x43, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00 };

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
                        int count = await ExtractBCFilesAsync(content, filePath, extractedDir, extractedFiles, cancellationToken);

                        if (count > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中提取出{count}个BC文件");
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
                    ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个BC文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到BC文件");
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

        private async Task<int> ExtractBCFilesAsync(byte[] content, string filePath, string extractedDir,
            List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int count = 0;
            int index = 0;

            while (index < content.Length)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int bcStartIndex = IndexOf(content, BC_START_SIGNATURE, index);
                if (bcStartIndex == -1)
                {
                    break;
                }
                int nextBcIndex = IndexOf(content, BC_START_SIGNATURE, bcStartIndex + BC_START_SIGNATURE.Length);
                int bcEndIndex = nextBcIndex == -1 ? content.Length : nextBcIndex;
                byte[] bcData = new byte[bcEndIndex - bcStartIndex];
                Array.Copy(content, bcStartIndex, bcData, 0, bcData.Length);

                count++;
                string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                string outputFileName = $"{baseFileName}_{count}.anm";
                string outputFilePath = Path.Combine(extractedDir, outputFileName);
                outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                try
                {
                    await File.WriteAllBytesAsync(outputFilePath, bcData, cancellationToken);
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)}");
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{ex.Message}");
                    OnExtractionFailed($"写入文件{outputFilePath}时出错:{ex.Message}");
                }
                index = bcEndIndex;
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
    }
}