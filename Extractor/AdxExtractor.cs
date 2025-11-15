namespace super_toolbox
{
    public class AdxExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] ADX_SIG_BYTES = { 0x80, 0x00 };
        private static readonly byte[] CRI_COPYRIGHT_BYTES = { 0x28, 0x63, 0x29, 0x43, 0x52, 0x49 };
        private static readonly byte[][] FIXED_SEQUENCES =
        {
            new byte[] { 0x03, 0x12, 0x04, 0x01, 0x00, 0x00 },
            new byte[] { 0x03, 0x12, 0x04, 0x02, 0x00, 0x00 }
        };

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
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase));
            TotalFilesToExtract = 0;
            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");
                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    int index = 0;
                    int? currentHeaderStart = null;
                    int innerCount = 1;

                    while (index < content.Length)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        int headerStartIndex = IndexOf(content, ADX_SIG_BYTES, index);
                        if (headerStartIndex == -1)
                        {
                            if (currentHeaderStart.HasValue)
                            {
                                ProcessAdxSegment(content, currentHeaderStart.Value, content.Length,
                                                filePath, innerCount, extractedDir, extractedFiles);
                            }
                            break;
                        }
                        int checkLength = Math.Min(10, content.Length - headerStartIndex);
                        byte[] checkSegment = new byte[checkLength];
                        Array.Copy(content, headerStartIndex, checkSegment, 0, checkLength);

                        if (ContainsBytes(checkSegment, FIXED_SEQUENCES[0]) ||
                            ContainsBytes(checkSegment, FIXED_SEQUENCES[1]))
                        {
                            if (!currentHeaderStart.HasValue)
                            {
                                currentHeaderStart = headerStartIndex;
                            }
                            else
                            {
                                ProcessAdxSegment(content, currentHeaderStart.Value, headerStartIndex,
                                                filePath, innerCount, extractedDir, extractedFiles);
                                innerCount++;
                                currentHeaderStart = headerStartIndex;
                            }
                        }
                        index = headerStartIndex + 1;
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
                    OnExtractionFailed($"读取文件{filePath} 时出错:{e.Message}");
                }
            }
            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个ADX文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到ADX文件");
            }
            OnExtractionCompleted();
        }
        private void ProcessAdxSegment(byte[] content, int start, int end, string filePath, int innerCount,
                                     string extractedDir, List<string> extractedFiles)
        {
            int length = end - start;
            if (length <= 0) return;
            byte[] adxData = new byte[length];
            Array.Copy(content, start, adxData, 0, length);

            if (!ContainsBytes(adxData, CRI_COPYRIGHT_BYTES))
                return;
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputFileName = $"{baseFileName}_{innerCount}.adx";
            string outputFilePath = Path.Combine(extractedDir, outputFileName);
            if (File.Exists(outputFilePath))
            {
                int duplicateCount = 1;
                do
                {
                    outputFileName = $"{baseFileName}_{innerCount}_dup{duplicateCount}.adx";
                    outputFilePath = Path.Combine(extractedDir, outputFileName);
                    duplicateCount++;
                } while (File.Exists(outputFilePath));
            }
            try
            {
                File.WriteAllBytes(outputFilePath, adxData);
                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
                }
            }
            catch (IOException e)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath} 时出错:{e.Message}");
                OnExtractionFailed($"写入文件{outputFilePath} 时出错:{e.Message}");
            }
        }
    }
}