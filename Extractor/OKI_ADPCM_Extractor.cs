namespace super_toolbox
{
    public class OKI_ADPCM_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] FILE_HEADER = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] FILE_FOOTER = new byte[] { 0x00, 0x07, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77 };
        private const byte OFFSET_0x11_MUST_BE = 0x04;
        private const byte PENULTIMATE_LINE_OFFSET_0x1_MUST_BE = 0x01;

        private const string TXTH_CONFIG = @"codec = PSX
            channels = 1
            sample_rate = 22050
            start_offset = 0x00
            num_samples = data_size";

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            List<string> createdTxthFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        int count = await ExtractOKI_ADPCM_FilesAsync(content, filePath, extractedFiles, createdTxthFiles, cancellationToken);

                        if (count > 0)
                        {
                            ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中提取出{count}个PCM文件");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个PCM文件");
                    ExtractionProgress?.Invoke(this, $"已创建{createdTxthFiles.Count}个.txth配置文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成,未找到OKI ADPCM文件");
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

        private async Task<int> ExtractOKI_ADPCM_FilesAsync(byte[] content, string filePath,
            List<string> extractedFiles, List<string> createdTxthFiles, CancellationToken cancellationToken)
        {
            int count = 0;
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string sourceDir = Path.GetDirectoryName(filePath) ?? "";
            string extractDir = Path.Combine(sourceDir, baseFileName);
            Directory.CreateDirectory(extractDir);

            var headerPositions = FindAllPositions(content, FILE_HEADER);
            var footerPositions = FindAllPositions(content, FILE_FOOTER);

            if (headerPositions.Count == 0 || footerPositions.Count == 0)
                return 0;

            for (int h = 0; h < headerPositions.Count; h++)
            {
                int headerPos = headerPositions[h];
                if (headerPos + 0x11 >= content.Length || content[headerPos + 0x11] != OFFSET_0x11_MUST_BE)
                    continue;
                if (HasTooManyConsecutiveZerosAfterHeader(content, headerPos))
                    continue;

                for (int f = 0; f < footerPositions.Count; f++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    int footerPos = footerPositions[f];
                    int footerEnd = footerPos + FILE_FOOTER.Length;

                    if (footerPos <= headerPos)
                        continue;

                    if (ValidateOKI_ADPCM_Structure(content, headerPos, footerEnd))
                    {
                        string pcmFilePath = await ExtractSegmentAsync(content, headerPos, footerEnd,
                            baseFileName, extractDir, ++count, extractedFiles, cancellationToken);

                        if (!string.IsNullOrEmpty(pcmFilePath) && File.Exists(pcmFilePath))
                        {
                            await CreateTxthConfigFileAsync(pcmFilePath, createdTxthFiles, cancellationToken);
                        }

                        footerPositions.RemoveRange(0, f + 1);
                        break;
                    }
                }
            }

            return count;
        }

        private bool HasTooManyConsecutiveZerosAfterHeader(byte[] content, int headerPos)
        {
            int startCheckPos = headerPos + 0x12;
            int checkLength = 256;
            if (startCheckPos + checkLength >= content.Length)
                return false;
            int maxConsecutiveZeros = 16;
            int consecutiveZeros = 0;
            for (int i = startCheckPos; i < Math.Min(startCheckPos + checkLength, content.Length); i++)
            {
                if (content[i] == 0x00)
                {
                    consecutiveZeros++;
                    if (consecutiveZeros > maxConsecutiveZeros)
                    {
                        return true;
                    }
                }
                else
                {
                    consecutiveZeros = 0;
                }
            }

            return false;
        }

        private async Task<string> ExtractSegmentAsync(byte[] content, int startIndex, int endIndex,
            string baseFileName, string extractDir, int segmentNumber,
            List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int length = endIndex - startIndex;
            if (length <= 0) return string.Empty;

            byte[] pcmData = new byte[length];
            Array.Copy(content, startIndex, pcmData, 0, length);

            string outputFileName = $"{baseFileName}_{segmentNumber}.pcm";
            string outputFilePath = Path.Combine(extractDir, outputFileName);

            outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

            try
            {
                await File.WriteAllBytesAsync(outputFilePath, pcmData, cancellationToken);
                extractedFiles.Add(outputFilePath);
                OnFileExtracted(outputFilePath);
                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)}");

                return outputFilePath;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{ex.Message}");
                OnExtractionFailed($"写入文件{outputFilePath}时出错:{ex.Message}");
                return string.Empty;
            }
        }

        private async Task CreateTxthConfigFileAsync(string pcmFilePath, List<string> createdTxthFiles,
            CancellationToken cancellationToken)
        {
            try
            {
                string txthFilePath = pcmFilePath + ".txth";

                if (!File.Exists(txthFilePath))
                {
                    await File.WriteAllTextAsync(txthFilePath, TXTH_CONFIG, cancellationToken);
                    createdTxthFiles.Add(txthFilePath);
                    ExtractionProgress?.Invoke(this, $"已创建配置文件:{Path.GetFileName(txthFilePath)}");
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"创建.txth配置文件时出错:{ex.Message}");
                OnExtractionFailed($"创建.txth配置文件时出错:{ex.Message}");
            }
        }

        private List<int> FindAllPositions(byte[] data, byte[] pattern)
        {
            List<int> positions = new List<int>();
            int index = 0;

            while (index < data.Length)
            {
                int foundIndex = IndexOf(data, pattern, index);
                if (foundIndex == -1) break;

                positions.Add(foundIndex);
                index = foundIndex + pattern.Length;
            }

            return positions;
        }

        private bool ValidateOKI_ADPCM_Structure(byte[] content, int startIndex, int endIndex)
        {
            if (endIndex > content.Length || startIndex >= endIndex) return false;

            int totalBytes = endIndex - startIndex;
            if (totalBytes % 16 != 0 || totalBytes < 64) return false;

            int totalLines = totalBytes / 16;

            int penultimateLineOffset = startIndex + ((totalLines - 2) * 16) + 1;
            if (penultimateLineOffset >= endIndex) return false;

            return content[penultimateLineOffset] == PENULTIMATE_LINE_OFFSET_0x1_MUST_BE;
        }

        private async Task<string> GenerateUniqueFilePathAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath)) return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;
            string newPath;

            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
                counter++;
                ThrowIfCancellationRequested(cancellationToken);
            }
            while (File.Exists(newPath));

            return newPath;
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
                if (found) return i;
            }
            return -1;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
        }
    }
}
