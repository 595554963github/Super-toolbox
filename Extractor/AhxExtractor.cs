namespace super_toolbox
{
    public class AhxExtractor : BaseExtractor
    {
        private static readonly byte[] AHX_START_HEADER = { 0x80, 0x00, 0x00, 0x20 };
        private static readonly byte[] AHX_END_HEADER = { 0x80, 0x01, 0x00, 0x0C, 0x41, 0x48, 0x58, 0x45, 0x28, 0x63, 0x29, 0x43, 0x52, 0x49, 0x00, 0x00 };

        public new event EventHandler<string>? ExtractionProgress;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"目录不存在:{directoryPath}");
                return;
            }

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            ExtractionProgress?.Invoke(this, $"开始从目录{directoryPath}提取AHX文件");

            try
            {
                var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);
                TotalFilesToExtract = 0; 

                foreach (string filePath in files)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    if (Path.GetExtension(filePath).Equals(".ahx", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                    try
                    {
                        var extractedCount = await ExtractAhxsFromFileAsync(filePath, extractedDir, cancellationToken);
                        TotalFilesToExtract += extractedCount;
                    }
                    catch (Exception ex)
                    {
                        OnExtractionFailed($"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"提取完成，共提取{ExtractedFileCount}个AHX文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取过程中发生错误:{ex.Message}");
            }
        }

        private async Task<int> ExtractAhxsFromFileAsync(string filePath, string extractedDir, CancellationToken cancellationToken)
        {
            int count = 0;

            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);
                string baseFilename = Path.GetFileNameWithoutExtension(filePath);
                string outputDir = Path.Combine(extractedDir, baseFilename);
                Directory.CreateDirectory(outputDir);

                foreach (byte[] ahxData in ExtractAhxData(fileContent))
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string extractedFilename = $"{baseFilename}_{count + 1}.ahx";
                    string extractedPath = Path.Combine(outputDir, extractedFilename);

                    await File.WriteAllBytesAsync(extractedPath, ahxData, cancellationToken);
                    OnFileExtracted(extractedPath);
                    count++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"处理文件{filePath}时出错: {ex.Message}");
                throw;
            }

            return count;
        }

        private static IEnumerable<byte[]> ExtractAhxData(byte[] fileContent)
        {
            int startIndex = 0;
            while ((startIndex = IndexOf(fileContent, AHX_START_HEADER, startIndex)) != -1)
            {
                int endIndex = IndexOf(fileContent, AHX_END_HEADER, startIndex + AHX_START_HEADER.Length);
                if (endIndex != -1)
                {
                    endIndex += AHX_END_HEADER.Length;
                    int length = endIndex - startIndex;
                    byte[] ahxData = new byte[length];
                    Array.Copy(fileContent, startIndex, ahxData, 0, length);
                    yield return ahxData;
                    startIndex = endIndex;
                }
                else
                {
                    break;
                }
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
    }
}