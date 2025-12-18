namespace super_toolbox
{
    public class WebpExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] RIFF_HEADER = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] WEBP_BLOCK = { 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38 };
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
                if (found) return i;
            }
            return -1;
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);
            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
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
                    int extractedFromThisFile = await ProcessFileContent(content, filePath, extractedDir, cancellationToken);
                    totalExtractedFiles += extractedFromThisFile;
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
                ExtractionProgress?.Invoke(this, $"处理完成，共处理{totalSourceFiles}个源文件，提取出{totalExtractedFiles}个WEBP文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共处理{totalSourceFiles}个源文件，未找到WEBP文件");
            }
            OnExtractionCompleted();
        }
        private async Task<int> ProcessFileContent(byte[] content, string filePath, string extractedDir, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            int index = 0;
            int? currentWebpStart = null;
            int webpCount = 1;
            while (index < content.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int webpStartIndex = IndexOf(content, RIFF_HEADER, index);
                if (webpStartIndex == -1)
                {
                    if (currentWebpStart.HasValue)
                    {
                        if (await ProcessWebpSegment(content, currentWebpStart.Value, content.Length, filePath, webpCount, extractedDir))
                        {
                            extractedCount++;
                        }
                    }
                    break;
                }
                if (IsValidWebpHeader(content, webpStartIndex))
                {
                    if (currentWebpStart.HasValue)
                    {
                        if (await ProcessWebpSegment(content, currentWebpStart.Value, webpStartIndex, filePath, webpCount, extractedDir))
                        {
                            extractedCount++;
                        }
                        webpCount++;
                    }
                    currentWebpStart = webpStartIndex;
                }
                index = webpStartIndex + 1;
            }
            if (currentWebpStart.HasValue)
            {
                if (await ProcessWebpSegment(content, currentWebpStart.Value, content.Length, filePath, webpCount, extractedDir))
                {
                    extractedCount++;
                }
            }
            return extractedCount;
        }
        private bool IsValidWebpHeader(byte[] content, int startIndex)
        {
            if (startIndex + 12 >= content.Length) return false;
            int fileSize = BitConverter.ToInt32(content, startIndex + 4);
            if (fileSize <= 0 || startIndex + 8 + fileSize > content.Length) return false;
            int blockStart = startIndex + 8;
            return IndexOf(content, WEBP_BLOCK, blockStart) != -1;
        }
        private async Task<bool> ProcessWebpSegment(byte[] content, int start, int end, string filePath, int webpCount, string extractedDir)
        {
            int length = end - start;
            if (length <= RIFF_HEADER.Length) return false;
            int actualLength = Math.Min(length, content.Length - start);
            byte[] webpData = new byte[actualLength];
            Array.Copy(content, start, webpData, 0, actualLength);
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputFilePath = GetUniqueFilePath(extractedDir, baseFileName, webpCount, "webp");
            try
            {
                await File.WriteAllBytesAsync(outputFilePath, webpData);
                OnFileExtracted(outputFilePath);
                return true;
            }
            catch (Exception)
            {
                if (File.Exists(outputFilePath))
                {
                    try { File.Delete(outputFilePath); } catch { }
                }
                return false;
            }
        }
        private string GetUniqueFilePath(string directory, string baseName, int count, string extension)
        {
            string fileName = $"{baseName}_{count}.{extension}";
            string filePath = Path.Combine(directory, fileName);
            if (!File.Exists(filePath)) return filePath;
            int duplicateCount = 1;
            do
            {
                fileName = $"{baseName}_{count}_dup{duplicateCount}.{extension}";
                filePath = Path.Combine(directory, fileName);
                duplicateCount++;
            } while (File.Exists(filePath));
            return filePath;
        }
    }
}
