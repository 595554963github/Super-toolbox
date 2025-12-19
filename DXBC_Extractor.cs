namespace super_toolbox
{
    public class DXBC_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] DXBC_SIGNATURE = { 0x44, 0x58, 0x42, 0x43 };
        private const int SIZE_OFFSET = 0x18;

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

            foreach (var filePath in filePaths)
            {
                processedFiles++;
                base.ThrowIfCancellationRequested(cancellationToken);

                string fileDir = Path.GetDirectoryName(filePath) ?? string.Empty;
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                string extractedDir = Path.Combine(fileDir, fileNameWithoutExt);
                Directory.CreateDirectory(extractedDir);

                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}({processedFiles}/{TotalFilesToExtract})");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    int dxbcCount = await ProcessFileForDXBC(content, filePath, extractedDir, extractedFiles, cancellationToken);

                    if (dxbcCount > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"在{Path.GetFileName(filePath)}中找到{dxbcCount}个DXBC文件");
                    }
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

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个DXBC文件");
                OnExtractionCompleted();
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到DXBC文件");
                OnExtractionCompleted();
            }
        }

        private async Task<int> ProcessFileForDXBC(byte[] content, string sourceFilePath, string extractedDir,
                                                 List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int dxbcCount = 0;
            int searchIndex = 0;
            string baseFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            List<int> signatureIndexes = new List<int>();
            while (searchIndex < content.Length)
            {
                base.ThrowIfCancellationRequested(cancellationToken);
                int sigIndex = FindPattern(content, DXBC_SIGNATURE, searchIndex);
                if (sigIndex == -1) break;

                signatureIndexes.Add(sigIndex);
                searchIndex = sigIndex + DXBC_SIGNATURE.Length;
            }
            for (int i = 0; i < signatureIndexes.Count; i++)
            {
                base.ThrowIfCancellationRequested(cancellationToken);
                int sigIndex = signatureIndexes[i];
                int fileSize = 0;

                if (i == signatureIndexes.Count - 1 || sigIndex + SIZE_OFFSET + 4 <= content.Length)
                {
                    fileSize = BitConverter.ToInt32(content, sigIndex + SIZE_OFFSET);
                }

                if (fileSize <= 0 || sigIndex + fileSize > content.Length)
                {
                    if (i != signatureIndexes.Count - 1)
                    {
                        fileSize = CalculateDynamicFileSize(content, sigIndex, signatureIndexes, i);
                    }
                    else
                    {
                        continue;
                    }
                }

                if (fileSize <= 0 || sigIndex + fileSize > content.Length)
                {
                    continue;
                }

                byte[] dxbcData = new byte[fileSize];
                Array.Copy(content, sigIndex, dxbcData, 0, fileSize);

                dxbcCount++;
                string outputFileName = $"{baseFileName}_{dxbcCount}.bin";
                string outputFilePath = Path.Combine(extractedDir, outputFileName);
                outputFilePath = GetUniqueFilePath(outputFilePath);

                await SaveDXBCFile(dxbcData, outputFilePath, extractedFiles);
            }

            return dxbcCount;
        }
        private int CalculateDynamicFileSize(byte[] content, int startIndex, List<int> signatureIndexes, int currentIndex)
        {
            if (currentIndex + 1 < signatureIndexes.Count)
            {
                return signatureIndexes[currentIndex + 1] - startIndex;
            }
            return content.Length - startIndex;
        }
        private int FindPattern(byte[] data, byte[] pattern, int startIndex)
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
                if (found) return i;
            }
            return -1;
        }
        private string GetUniqueFilePath(string originalPath)
        {
            if (!File.Exists(originalPath)) return originalPath;

            string directory = Path.GetDirectoryName(originalPath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);

            int duplicateCount = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExtension}_{duplicateCount}{extension}");
                duplicateCount++;
            } while (File.Exists(newPath));

            return newPath;
        }
        private async Task SaveDXBCFile(byte[] data, string outputFilePath, List<string> extractedFiles)
        {
            try
            {
                await File.WriteAllBytesAsync(outputFilePath, data);

                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)}");
                }
            }
            catch (IOException e)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                OnExtractionFailed($"写入文件{outputFilePath}时出错:{e.Message}");
            }
        }
    }
}