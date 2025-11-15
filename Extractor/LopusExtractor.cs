namespace super_toolbox
{
    public class LopusExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] OPUS_HEADER = { 0x4F, 0x50, 0x55, 0x53 };
        private static readonly byte[] LOPUS_HEADER = { 0x01, 0x00, 0x00, 0x80, 0x18, 0x00, 0x00, 0x00 };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"目录{directoryPath}不存在");
                OnExtractionFailed($"目录{directoryPath}不存在");
                return;
            }
            try
            {
                var allFiles = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).Equals("Extracted", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                TotalFilesToExtract = allFiles.Count;
                ExtractionStarted?.Invoke(this, $"开始处理{allFiles.Count}个文件");
                await Task.Run(() => ProcessFiles(allFiles, directoryPath, cancellationToken), cancellationToken);
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理目录时出错: {ex.Message}");
                OnExtractionFailed($"处理目录时出错: {ex.Message}");
            }
        }
        private void ProcessFiles(List<string> files, string baseDir, CancellationToken cancellationToken)
        {
            var outputDir = Path.Combine(baseDir, "Extracted");
            Directory.CreateDirectory(outputDir);
            int processedCount = 0;
            int totalExtractedFiles = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedCount++;
                ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(file)} ({processedCount}/{files.Count})");
                try
                {
                    int extractedFromFile = 0;

                    if (IsOpusFile(file))
                    {
                        extractedFromFile = ProcessOpusFile(file, outputDir);
                    }
                    else
                    {
                        extractedFromFile = ProcessOtherFile(file, outputDir);
                    }
                    totalExtractedFiles += extractedFromFile;
                    if (extractedFromFile > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(file)}中提取了{extractedFromFile}个文件");
                    }
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(file)}时出错:{ex.Message}");
                }
            }
            ExtractionProgress?.Invoke(this, $"提取完成，总共提取了{totalExtractedFiles}个.lopus文件");
        }
        private int ProcessOpusFile(string filePath, string outputDir)
        {
            var content = File.ReadAllBytes(filePath);

            if (!IsValidOpus(content))
            {
                ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(filePath)}不是有效的OPUS文件");
                return 0;
            }
            if (TryExtractLopusData(content, out var lopusData) && lopusData != null)
            {
                string outputPath = SaveLopusFile(lopusData, Path.GetFileNameWithoutExtension(filePath), "", outputDir);
                OnFileExtracted(outputPath);
                return 1;
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"警告:{Path.GetFileName(filePath)}未找到LOPUS头，跳过处理");
                return 0;
            }
        }
        private int ProcessOtherFile(string filePath, string outputDir)
        {
            var content = File.ReadAllBytes(filePath);
            var opusSegments = FindOpusSegments(content);

            if (!opusSegments.Any())
                return 0;
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            ExtractionProgress?.Invoke(this, $"在{baseName} 中发现{opusSegments.Count}个OPUS片段");
            int extractedCount = 0;
            for (int i = 0; i < opusSegments.Count; i++)
            {
                if (TryExtractLopusData(opusSegments[i], out var lopusData) && lopusData != null)
                {
                    string outputPath = SaveLopusFile(lopusData, baseName, $"_{i + 1}", outputDir);

                    OnFileExtracted(outputPath);
                    extractedCount++;
                }
                else
                {
                    ExtractionProgress?.Invoke(this, $"警告:{baseName}_{i + 1}未找到LOPUS头，跳过处理");
                }
            }
            return extractedCount;
        }
        private string SaveLopusFile(byte[] data, string baseName, string suffix, string outputDir)
        {
            string fileName = $"{baseName}{suffix}.lopus";
            string path = Path.Combine(outputDir, fileName);
            File.WriteAllBytes(path, data);
            return path;
        }
        private bool TryExtractLopusData(byte[] opusData, out byte[]? lopusData)
        {
            lopusData = null;
            int pos = FindHeaderPosition(opusData, LOPUS_HEADER, 0);

            if (pos == -1) return false;

            lopusData = new byte[opusData.Length - pos];
            Array.Copy(opusData, pos, lopusData, 0, lopusData.Length);
            return true;
        }
        private List<byte[]> FindOpusSegments(byte[] data)
        {
            var segments = new List<byte[]>();
            var positions = FindAllHeaderPositions(data, OPUS_HEADER);

            for (int i = 0; i < positions.Count; i++)
            {
                int start = positions[i];
                int end = (i < positions.Count - 1) ? positions[i + 1] : data.Length;
                var segment = new byte[end - start];
                Array.Copy(data, start, segment, 0, segment.Length);
                segments.Add(segment);
            }
            return segments;
        }
        #region Helper Methods
        private bool IsOpusFile(string filePath) =>
            Path.GetExtension(filePath).Equals(".opus", StringComparison.OrdinalIgnoreCase);
        private bool IsValidOpus(byte[] data) =>
            data.Length >= OPUS_HEADER.Length && CheckHeader(data, 0, OPUS_HEADER);
        private List<int> FindAllHeaderPositions(byte[] data, byte[] header)
        {
            var positions = new List<int>();
            for (int offset = 0; ; offset += header.Length)
            {
                offset = FindHeaderPosition(data, header, offset);
                if (offset == -1) break;
                positions.Add(offset);
            }
            return positions;
        }
        private int FindHeaderPosition(byte[] data, byte[] header, int startIndex)
        {
            int endIndex = data.Length - header.Length;
            for (int i = startIndex; i <= endIndex; i++)
                if (CheckHeader(data, i, header))
                    return i;
            return -1;
        }
        private bool CheckHeader(byte[] data, int startIndex, byte[] header)
        {
            for (int j = 0; j < header.Length; j++)
                if (data[startIndex + j] != header[j])
                    return false;
            return true;
        }
        #endregion
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}