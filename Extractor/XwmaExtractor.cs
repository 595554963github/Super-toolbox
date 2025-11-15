namespace super_toolbox
{
    public class XwmaExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] RIFF_HEADER = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] XWMA_BLOCK = { 0x58, 0x57, 0x4D, 0x41, 0x66, 0x6D, 0x74 };

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

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            ExtractionProgress?.Invoke(this, $"找到 {TotalFilesToExtract} 个源文件");

            List<string> extractedFiles = new List<string>();

            foreach (string filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);

                ExtractionProgress?.Invoke(this, $"正在处理文件: {Path.GetFileName(filePath)}");

                try
                {
                    await ExtractXwmasFromFileAsync(filePath, extractedDir, extractedFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件 {Path.GetFileName(filePath)} 时出错: {ex.Message}");
                    OnExtractionFailed($"处理文件 {Path.GetFileName(filePath)} 时出错: {ex.Message}");
                }
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出 {extractedFiles.Count} 个XWMA文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到XWMA文件");
            }

            OnExtractionCompleted();
        }

        private async Task ExtractXwmasFromFileAsync(string filePath, string extractedDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            byte[] fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);
            string baseFilename = Path.GetFileNameWithoutExtension(filePath);
            int innerCount = 1;

            foreach (byte[] xwmaData in ExtractXwmaData(fileContent))
            {
                ThrowIfCancellationRequested(cancellationToken);

                string extractedFilename = $"{baseFilename}_{innerCount}.xwma";
                string extractedPath = Path.Combine(extractedDir, extractedFilename);

                // 处理重复文件名
                if (File.Exists(extractedPath))
                {
                    int duplicateCount = 1;
                    do
                    {
                        extractedFilename = $"{baseFilename}_{innerCount}_dup{duplicateCount}.xwma";
                        extractedPath = Path.Combine(extractedDir, extractedFilename);
                        duplicateCount++;
                    } while (File.Exists(extractedPath));
                }

                await File.WriteAllBytesAsync(extractedPath, xwmaData, cancellationToken);

                if (!extractedFiles.Contains(extractedPath))
                {
                    extractedFiles.Add(extractedPath);
                    OnFileExtracted(extractedPath);
                    ExtractionProgress?.Invoke(this, $"已提取: {Path.GetFileName(extractedPath)}");
                }

                innerCount++;
            }
        }

        private static IEnumerable<byte[]> ExtractXwmaData(byte[] fileContent)
        {
            int xwmaDataStart = 0;
            while ((xwmaDataStart = IndexOf(fileContent, RIFF_HEADER, xwmaDataStart)) != -1)
            {
                if (xwmaDataStart + 12 > fileContent.Length)
                {
                    xwmaDataStart += 4;
                    continue;
                }

                int fileSize = BitConverter.ToInt32(fileContent, xwmaDataStart + 4);
                fileSize = (fileSize + 1) & ~1;

                if (fileSize <= 0 || xwmaDataStart + 8 + fileSize > fileContent.Length)
                {
                    xwmaDataStart += 4;
                    continue;
                }

                int blockStart = xwmaDataStart + 8;
                bool hasXwmaBlock = IndexOf(fileContent, XWMA_BLOCK, blockStart) != -1;

                if (hasXwmaBlock)
                {
                    int actualLength = Math.Min(fileSize + 8, fileContent.Length - xwmaDataStart);
                    byte[] xwmaData = new byte[actualLength];
                    Array.Copy(fileContent, xwmaDataStart, xwmaData, 0, actualLength);
                    yield return xwmaData;
                }

                xwmaDataStart += Math.Max(4, fileSize + 8);
            }
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

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("提取操作已取消", cancellationToken);
            }
        }
    }
}