namespace super_toolbox
{
    public class Msf_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] MSF_HEADER = { 0x4D, 0x53, 0x46, 0x43 }; // "MSFC"
        private static readonly int REQUIRED_TRAILING_ZEROS = 148;

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
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase) &&
                              !Path.GetExtension(file).Equals(".msf", StringComparison.OrdinalIgnoreCase));

            TotalFilesToExtract = 0;
            int totalExtractedCount = 0;

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);

                    List<int> headerPositions = FindAllHeaderPositions(content);

                    if (headerPositions.Count == 0)
                    {
                        ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(filePath)}中未找到MSF头");
                        continue;
                    }

                    int msfCount = 0;

                    for (int i = 0; i < headerPositions.Count; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        int startIndex = headerPositions[i];
                        int endIndex = (i < headerPositions.Count - 1) ? headerPositions[i + 1] : content.Length;

                        byte[]? msfData = ExtractAndFixMsfData(content, startIndex, endIndex);

                        if (msfData != null && msfData.Length > 0)
                        {
                            msfCount++;
                            totalExtractedCount++;

                            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputFileName = $"{baseFileName}_{msfCount}.msf";
                            string outputFilePath = Path.Combine(extractedDir, outputFileName);

                            outputFilePath = GetUniqueFilePath(outputFilePath);

                            await File.WriteAllBytesAsync(outputFilePath, msfData, cancellationToken);

                            if (!extractedFiles.Contains(outputFilePath))
                            {
                                extractedFiles.Add(outputFilePath);
                                OnFileExtracted(outputFilePath);
                                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)} (大小:{msfData.Length}字节)");
                            }
                        }
                    }

                    if (msfCount > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中提取出{msfCount}个MSF文件");
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
                    ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{filePath}时出错:{e.Message}");
                }
            }

            TotalFilesToExtract = extractedFiles.Count;

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个MSF文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到MSF文件");
            }
        }

        private byte[]? ExtractAndFixMsfData(byte[] content, int startIndex, int endIndex)
        {
            if (startIndex >= endIndex) return null;

            int length = endIndex - startIndex;
            byte[] msfData = new byte[length];
            Array.Copy(content, startIndex, msfData, 0, length);

            if (msfData.Length <= REQUIRED_TRAILING_ZEROS)
            {
                ExtractionProgress?.Invoke(this, $"警告:MSF数据长度({msfData.Length}字节)小于所需尾部00字节数({REQUIRED_TRAILING_ZEROS})");
                return msfData;
            }

            int trailingZerosStartIndex = FindTrailingZerosStartIndex(msfData);

            if (trailingZerosStartIndex != -1)
            {
                int newLength = trailingZerosStartIndex + REQUIRED_TRAILING_ZEROS;
                byte[] fixedMsfData = new byte[newLength];
                Array.Copy(msfData, 0, fixedMsfData, 0, newLength);

                int removedBytes = msfData.Length - newLength;
                if (removedBytes > 0)
                {
                    ExtractionProgress?.Invoke(this, $"已删除文件尾{removedBytes}个多余字节");
                }

                return fixedMsfData;
            }
            else
            {
                int newLength = msfData.Length - (msfData.Length - REQUIRED_TRAILING_ZEROS);
                byte[] fixedMsfData = new byte[newLength];
                Array.Copy(msfData, 0, fixedMsfData, 0, newLength - REQUIRED_TRAILING_ZEROS);

                for (int i = newLength - REQUIRED_TRAILING_ZEROS; i < newLength; i++)
                {
                    fixedMsfData[i] = 0x00;
                }

                ExtractionProgress?.Invoke(this, "未找到连续的148个00字节，已在尾部填充00");
                return fixedMsfData;
            }
        }

        private int FindTrailingZerosStartIndex(byte[] data)
        {
            for (int i = data.Length - REQUIRED_TRAILING_ZEROS; i >= 0; i--)
            {
                bool isAllZero = true;
                for (int j = 0; j < REQUIRED_TRAILING_ZEROS; j++)
                {
                    if (data[i + j] != 0x00)
                    {
                        isAllZero = false;
                        break;
                    }
                }
                if (isAllZero)
                {
                    return i;
                }
            }
            return -1;
        }

        private List<int> FindAllHeaderPositions(byte[] data)
        {
            List<int> positions = new List<int>();
            int index = 0;

            while (index <= data.Length - MSF_HEADER.Length)
            {
                int headerIndex = IndexOf(data, MSF_HEADER, index);
                if (headerIndex == -1)
                    break;

                positions.Add(headerIndex);
                index = headerIndex + MSF_HEADER.Length;
            }

            return positions;
        }

        private new static int IndexOf(byte[] data, byte[] pattern, int startIndex)
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

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return filePath;
            }

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);
            int duplicateCount = 1;
            string newFilePath;

            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_dup{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }
    }
}
