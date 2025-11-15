namespace super_toolbox
{
    public class RifxExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] RIFXHeader = { 0x52, 0x49, 0x46, 0x58 };
        private static readonly byte[] wemBlock = { 0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74 };
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
                        int headerStartIndex = IndexOf(content, RIFXHeader, index);
                        if (headerStartIndex == -1)
                        {
                            if (currentHeaderStart.HasValue)
                            {
                                ProcessWemSegment(content, currentHeaderStart.Value, content.Length,
                                                filePath, innerCount, extractedDir, extractedFiles);
                            }
                            break;
                        }
                        int nextRifxIndex = IndexOf(content, RIFXHeader, headerStartIndex + 1);
                        int endIndex = nextRifxIndex != -1 ? nextRifxIndex : content.Length;
                        int blockStart = headerStartIndex + 8;
                        bool hasWemBlock = ContainsBytes(content, wemBlock, blockStart);
                        if (hasWemBlock)
                        {
                            if (!currentHeaderStart.HasValue)
                            {
                                currentHeaderStart = headerStartIndex;
                            }
                            else
                            {
                                ProcessWemSegment(content, currentHeaderStart.Value, headerStartIndex,
                                                filePath, innerCount, extractedDir, extractedFiles);
                                innerCount++;
                                currentHeaderStart = headerStartIndex;
                            }
                        }
                        index = headerStartIndex + 1;
                    }
                    if (currentHeaderStart.HasValue)
                    {
                        ProcessWemSegment(content, currentHeaderStart.Value, content.Length,
                                        filePath, innerCount, extractedDir, extractedFiles);
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
            }
            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个WEM文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到WEM文件");
            }
            OnExtractionCompleted();
        }
        private void ProcessWemSegment(byte[] content, int start, int end, string filePath, int innerCount,
                                     string extractedDir, List<string> extractedFiles)
        {
            int length = end - start;
            if (length <= 0) return;

            byte[] wemData = new byte[length];
            Array.Copy(content, start, wemData, 0, length);
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputFileName = $"{baseFileName}_{innerCount}.wem";
            string outputFilePath = Path.Combine(extractedDir, outputFileName);
            if (File.Exists(outputFilePath))
            {
                int duplicateCount = 1;
                do
                {
                    outputFileName = $"{baseFileName}_{innerCount}_dup{duplicateCount}.wem";
                    outputFilePath = Path.Combine(extractedDir, outputFileName);
                    duplicateCount++;
                } while (File.Exists(outputFilePath));
            }
            try
            {
                File.WriteAllBytes(outputFilePath, wemData);
                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
                }
            }
            catch (IOException e)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                OnExtractionFailed($"写入文件{outputFilePath} 时出错:{e.Message}");
            }
        }
        private static bool ContainsBytes(byte[] data, byte[] pattern, int startIndex)
        {
            return IndexOf(data, pattern, startIndex) != -1;
        }
    }
}