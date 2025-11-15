namespace super_toolbox
{
    public class BankExtractor : BaseExtractor
    {
        private static readonly byte[] riffHeader = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] bankBlock = { 0x46, 0x45, 0x56, 0x20, 0x46, 0x4D, 0x54 };

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static List<byte[]> ExtractBankData(byte[] fileContent)
        {
            List<byte[]> bankDataList = new List<byte[]>();
            int bankDataStart = 0;

            while ((bankDataStart = IndexOf(fileContent, riffHeader, bankDataStart)) != -1)
            {
                try
                {
                    int fileSize = BitConverter.ToInt32(fileContent, bankDataStart + 4);
                    fileSize = (fileSize + 1) & ~1;

                    int blockStart = bankDataStart + 8;
                    bool hasBankBlock = IndexOf(fileContent, bankBlock, blockStart) != -1;

                    if (hasBankBlock)
                    {
                        byte[] bankData = new byte[fileSize + 8];
                        Array.Copy(fileContent, bankDataStart, bankData, 0, fileSize + 8);
                        bankDataList.Add(bankData);
                    }

                    bankDataStart += fileSize + 8;
                }
                catch (Exception ex)
                {
                    throw new Exception($"提取bank数据时出错:{ex.Message}");
                }
            }
            return bankDataList;
        }

        private static int IndexOf(byte[] source, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= source.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
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

        private List<string> ExtractBanksFromFile(string filePath, string extractedDir, CancellationToken cancellationToken)
        {
            List<string> extractedFileNames = new List<string>();

            try
            {
                byte[] fileContent = File.ReadAllBytes(filePath);
                List<byte[]> bankDataList = ExtractBankData(fileContent);
                int count = 0;

                foreach (byte[] bankData in bankDataList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string baseFilename = Path.GetFileNameWithoutExtension(filePath);
                    string extractedFilename = $"{baseFilename}_{count + 1}.bank";
                    string extractedPath = Path.Combine(extractedDir, extractedFilename);

                    if (File.Exists(extractedPath))
                    {
                        int duplicateCount = 1;
                        do
                        {
                            extractedFilename = $"{baseFilename}_{count}_dup{duplicateCount}.bank";
                            extractedPath = Path.Combine(extractedDir, extractedFilename);
                            duplicateCount++;
                        } while (File.Exists(extractedPath));
                    }

                    string? directory = Path.GetDirectoryName(extractedPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllBytes(extractedPath, bankData);
                    extractedFileNames.Add(extractedPath);

                    string relativePath = Path.GetRelativePath(extractedDir, extractedPath);
                    OnFileExtracted(relativePath);
                    ExtractionProgress?.Invoke(this, $"已提取文件: {relativePath}");

                    count++;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理文件 {filePath} 时出错: {ex.Message}");
                OnExtractionFailed($"处理文件 {filePath} 时出错: {ex.Message}");
            }

            return extractedFileNames;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"目录不存在: {directoryPath}");
                OnExtractionFailed($"目录不存在: {directoryPath}");
                return;
            }

            try
            {
                string extractedDir = Path.Combine(directoryPath, "Extracted");
                Directory.CreateDirectory(extractedDir);

                ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");
                ExtractionProgress?.Invoke(this, $"输出目录: {extractedDir}");

                string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

                TotalFilesToExtract = 0;
                foreach (string filePath in files)
                {
                    if (!Path.GetExtension(filePath).Equals(".bank", StringComparison.OrdinalIgnoreCase))
                    {
                        TotalFilesToExtract++;
                    }
                }
                int processedFiles = 0;
                foreach (string filePath in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (Path.GetExtension(filePath).Equals(".bank", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ExtractionProgress?.Invoke(this, $"\n正在处理文件: {Path.GetFileName(filePath)}");

                    var extractedFiles = await Task.Run(() =>
                        ExtractBanksFromFile(filePath, extractedDir, cancellationToken), cancellationToken);

                    processedFiles++;
                    ExtractionProgress?.Invoke(this, $"进度: {processedFiles}/{TotalFilesToExtract} 文件");
                }

                ExtractionProgress?.Invoke(this, $"\n处理完成，共提取出 {ExtractedFileCount} 个bank文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程中发生错误: {ex.Message}");
                OnExtractionFailed($"提取过程中发生错误: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}