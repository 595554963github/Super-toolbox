namespace super_toolbox
{
    public class VagExtractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private static readonly byte[] VAG_START_MARKER = { 0x56, 0x41, 0x47, 0x70 };

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
            !Path.GetExtension(file).Equals(".vag", StringComparison.OrdinalIgnoreCase))
            .ToList();

            int totalSourceFiles = filePaths.Count;
            int processedSourceFiles = 0;
            int totalExtractedFiles = 0;

            TotalFilesToExtract = totalSourceFiles;

            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedSourceFiles++;

                ExtractionProgress?.Invoke(this, $"正在处理源文件({processedSourceFiles}/{totalSourceFiles}):{Path.GetFileName(filePath)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    int index = 0;
                    int? currentVagStart = null;
                    int vagCount = 1;

                    while (index < content.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int vagStartIndex = IndexOf(content, VAG_START_MARKER, index);
                        if (vagStartIndex == -1)
                        {
                            if (currentVagStart.HasValue)
                            {
                                if (ProcessVagSegment(content, currentVagStart.Value, content.Length,
                                filePath, vagCount, extractedDir, extractedFiles))
                                {
                                    totalExtractedFiles++;
                                }
                                vagCount++;
                            }
                            break;
                        }

                        if (IsValidVagHeader(content, vagStartIndex))
                        {
                            if (!currentVagStart.HasValue)
                            {
                                currentVagStart = vagStartIndex;
                            }
                            else
                            {
                                if (ProcessVagSegment(content, currentVagStart.Value, vagStartIndex,
                                filePath, vagCount, extractedDir, extractedFiles))
                                {
                                    totalExtractedFiles++;
                                }
                                vagCount++;
                                currentVagStart = vagStartIndex;
                            }
                        }
                        index = vagStartIndex + 1;
                    }

                    if (currentVagStart.HasValue)
                    {
                        if (ProcessVagSegment(content, currentVagStart.Value, content.Length,
                        filePath, vagCount, extractedDir, extractedFiles))
                        {
                            totalExtractedFiles++;
                        }
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

            if (totalExtractedFiles > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共处理{totalSourceFiles}个源文件,提取出{totalExtractedFiles}个VAG文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共处理{totalSourceFiles}个源文件,未找到VAG文件");
            }

            OnExtractionCompleted();
        }

        private bool IsValidVagHeader(byte[] content, int startIndex)
        {
            if (startIndex + 16 >= content.Length)
                return false;
            return true;
        }

        private bool ProcessVagSegment(byte[] content, int start, int end, string filePath, int vagCount,
        string extractedDir, List<string> extractedFiles)
        {
            int defaultLength = end - start;
            if (defaultLength <= VAG_START_MARKER.Length)
                return false;

            int vagDataSize = GetVagSizeFromHeader(content, start);
            if (vagDataSize > 0)
            {
                int calculatedEnd = start + vagDataSize;
                if (calculatedEnd <= content.Length)
                {
                    defaultLength = vagDataSize;
                }
            }

            byte[] vagData = new byte[defaultLength];
            Array.Copy(content, start, vagData, 0, defaultLength);

            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputFileName = $"{baseFileName}_{vagCount}.vag";
            string outputFilePath = Path.Combine(extractedDir, outputFileName);

            if (File.Exists(outputFilePath))
            {
                int duplicateCount = 1;
                do
                {
                    outputFileName = $"{baseFileName}_{vagCount}_dup{duplicateCount}.vag";
                    outputFilePath = Path.Combine(extractedDir, outputFileName);
                    duplicateCount++;
                } while (File.Exists(outputFilePath));
            }

            try
            {
                File.WriteAllBytes(outputFilePath, vagData);
                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
                    return true;
                }
            }
            catch (IOException e)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                OnExtractionFailed($"写入文件{outputFilePath}时出错:{e.Message}");
            }

            return false;
        }

        private int GetVagSizeFromHeader(byte[] data, int startIndex)
        {
            if (startIndex + 16 > data.Length)
                return -1;

            int size = (data[startIndex + 0x0C] << 24) |
            (data[startIndex + 0x0D] << 16) |
            (data[startIndex + 0x0E] << 8) |
            (data[startIndex + 0x0F]);

            if (size > 0)
            {
                return size + 48;
            }

            return -1;
        }
    }
}