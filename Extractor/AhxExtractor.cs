namespace super_toolbox
{
    public class AhxExtractor : BaseExtractor
    {
        private static readonly byte[] AHX_START_HEADER = { 0x80, 0x00, 0x00, 0x20 };
        private static readonly byte[] AHX_END_HEADER = { 0x80, 0x01, 0x00, 0x0C, 0x41, 0x48, 0x58, 0x45, 0x28, 0x63, 0x29, 0x43, 0x52, 0x49, 0x00, 0x00 };

        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            var sourceFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !Path.GetExtension(file).Equals(".ahx", StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = sourceFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{sourceFiles.Count}个源文件");

            try
            {
                await Task.Run(() =>
                {
                    int totalExtractedAhxFiles = 0;

                    foreach (var sourceFilePath in sourceFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(sourceFilePath)}");

                            string sourceDir = Path.GetDirectoryName(sourceFilePath) ?? string.Empty;
                            string sourceFileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFilePath);
                            string extractedDir = Path.Combine(sourceDir, sourceFileNameWithoutExt);
                            Directory.CreateDirectory(extractedDir);

                            int extractedCount = ExtractAhxsFromFile(sourceFilePath, extractedDir, cancellationToken);
                            totalExtractedAhxFiles += extractedCount;

                            if (extractedCount > 0)
                            {
                                ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(sourceFilePath)}中提取出{extractedCount}个AHX文件");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(sourceFilePath)}时出错:{ex.Message}");
                            OnExtractionFailed($"处理{Path.GetFileName(sourceFilePath)}时出错:{ex.Message}");
                        }
                    }

                    if (totalExtractedAhxFiles > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"处理完成,共从{sourceFiles.Count}个源文件中提取出{totalExtractedAhxFiles}个AHX文件");
                    }
                    else
                    {
                        ExtractionProgress?.Invoke(this, "处理完成,未找到AHX文件");
                    }
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        private int ExtractAhxsFromFile(string filePath, string outputDir, CancellationToken cancellationToken)
        {
            int count = 0;

            try
            {
                byte[] fileContent = File.ReadAllBytes(filePath);
                string baseFilename = Path.GetFileNameWithoutExtension(filePath);
                foreach (byte[] ahxData in ExtractAhxData(fileContent))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string extractedFilename = $"{baseFilename}_{count + 1}.ahx";
                    string extractedPath = Path.Combine(outputDir, extractedFilename);

                    File.WriteAllBytes(extractedPath, ahxData);

                    OnFileExtracted(extractedPath);
                    count++;

                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(extractedPath)}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
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
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
