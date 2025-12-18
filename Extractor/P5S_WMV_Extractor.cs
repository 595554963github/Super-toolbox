namespace super_toolbox
{
    public class P5S_WMV_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] WMV_START_SEQ = { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11 };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            int totalExtractedCount = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                    try
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        int extractedCount = ExtractWmvFiles(content, filePath, extractedDir);
                        totalExtractedCount += extractedCount;

                        if (extractedCount > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中提取出{extractedCount}个WMV文件");
                        }
                    }
                    catch (OperationCanceledException)
                    {
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

                if (totalExtractedCount > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成，共提取出{totalExtractedCount}个WMV文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到WMV文件");
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

        private int ExtractWmvFiles(byte[] content, string filePath, string extractedDir)
        {
            int count = 0;
            int index = 0;

            while (index < content.Length)
            {
                int headerStartIndex = IndexOf(content, WMV_START_SEQ, index);
                if (headerStartIndex == -1)
                {
                    break;
                }

                int nextHeaderIndex = IndexOf(content, WMV_START_SEQ, headerStartIndex + 1);
                int endIndex = nextHeaderIndex == -1 ? content.Length : nextHeaderIndex;

                if (endIndex - headerStartIndex < 100) 
                {
                    index = headerStartIndex + 1;
                    continue;
                }

                byte[] extractedData = new byte[endIndex - headerStartIndex];
                Array.Copy(content, headerStartIndex, extractedData, 0, extractedData.Length);

                string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                string outputFileName = $"{baseFileName}_{++count}.wmv";
                string outputFilePath = Path.Combine(extractedDir, outputFileName);

                if (File.Exists(outputFilePath))
                {
                    int duplicateCount = 1;
                    do
                    {
                        outputFileName = $"{baseFileName}_{count}_dup{duplicateCount}.wmv";
                        outputFilePath = Path.Combine(extractedDir, outputFileName);
                        duplicateCount++;
                    } while (File.Exists(outputFilePath));
                }

                try
                {
                    File.WriteAllBytes(outputFilePath, extractedData);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
                }
                catch (IOException e)
                {
                    ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                    OnExtractionFailed($"写入文件{outputFilePath}时出错:{e.Message}");
                }

                index = headerStartIndex + 1;
            }

            return count;
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

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
