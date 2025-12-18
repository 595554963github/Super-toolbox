namespace super_toolbox
{
    public class Kvs_Kns_Extractor : BaseExtractor
    {
        private static readonly byte[] KVS_SIG_BYTES = { 0x4B, 0x4F, 0x56, 0x53 }; // Steam平台 "KOVS"
        private static readonly byte[] KNS_SIG_BYTES = { 0x4B, 0x54, 0x53, 0x53 }; // Switch平台 "KTSS"
        private static readonly byte[] AT3_SIG_BYTES = { 0x52, 0x49, 0x46, 0x46 }; // PS4平台(AT3) "RIFF"
        private static readonly byte[] KTAC_SIG_BYTES = { 0x4B, 0x54, 0x41, 0x43 }; // PS4平台 "KTAC"
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
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
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }
            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.Contains("Extracted"))
                .ToList();
            if (filePaths.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何文件");
                OnExtractionFailed("未找到任何文件");
                return;
            }
            TotalFilesToExtract = filePaths.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{filePaths.Count}个文件");

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);
            try
            {
                int processedCount = 0;
                int totalExtractedFiles = 0;
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedCount++;
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");
                    try
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        int kvsCount = CountSignature(content, KVS_SIG_BYTES);
                        int knsCount = CountSignature(content, KNS_SIG_BYTES);
                        int at3Count = CountSignature(content, AT3_SIG_BYTES);
                        int ktacCount = CountSignature(content, KTAC_SIG_BYTES);
                        ExtractionProgress?.Invoke(this, $"检测到:KOVS={kvsCount}, KTSS={knsCount}, RIFF={at3Count}, KTAC={ktacCount}");
                        var formatCounts = new Dictionary<byte[], int>
                        {
                            { KVS_SIG_BYTES, kvsCount },
                            { KNS_SIG_BYTES, knsCount },
                            { AT3_SIG_BYTES, at3Count },
                            { KTAC_SIG_BYTES, ktacCount }
                        };
                        var selectedFormat = formatCounts.OrderByDescending(x => x.Value).First();
                        if (selectedFormat.Value > 0)
                        {
                            string formatName = GetFormatName(selectedFormat.Key);
                            string extension = GetExtension(selectedFormat.Key);

                            ExtractionProgress?.Invoke(this, $"选择格式:{formatName}(数量:{selectedFormat.Value})");

                            int extractedCount = ExtractByFormat(content, filePath, extractedDir,
                                selectedFormat.Key, extension);
                            totalExtractedFiles += extractedCount;
                            ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}提取出{extractedCount}个{formatName}文件");
                        }
                        else
                        {
                            ExtractionProgress?.Invoke(this, $"{Path.GetFileName(filePath)} - 未找到音频数据");
                        }
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"文件{Path.GetFileName(filePath)}处理错误:{ex.Message}");
                    }
                }
                if (totalExtractedFiles > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成，共提取出{totalExtractedFiles}个音频文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到音频文件");
                }

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
        private int CountSignature(byte[] content, byte[] signature)
        {
            int count = 0;
            int index = 0;
            while (index < content.Length)
            {
                index = IndexOf(content, signature, index);
                if (index == -1) break;

                count++;
                index += signature.Length;
            }
            return count;
        }
        private int ExtractByFormat(byte[] content, string sourcePath, string outputDir,
                                  byte[] signature, string extension)
        {
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            int count = 0;
            int startIndex = 0;
            while (startIndex < content.Length)
            {
                int currentStart = IndexOf(content, signature, startIndex);
                if (currentStart == -1) break;
                int nextStart = IndexOf(content, signature, currentStart + signature.Length);
                if (nextStart == -1)
                    nextStart = content.Length;
                byte[] segmentData = new byte[nextStart - currentStart];
                Array.Copy(content, currentStart, segmentData, 0, nextStart - currentStart);
                count++;
                string fileName = $"{baseName}_{count}{extension}";
                string outputPath = Path.Combine(outputDir, fileName);
                try
                {
                    File.WriteAllBytes(outputPath, segmentData);
                    OnFileExtracted(outputPath);
                    ExtractionProgress?.Invoke(this, $"已提取:{fileName}");
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"保存{fileName}失败:{ex.Message}");
                }

                startIndex = nextStart;
            }
            return count;
        }
        private string GetFormatName(byte[] signature)
        {
            if (signature.SequenceEqual(KVS_SIG_BYTES)) return "KVS";
            if (signature.SequenceEqual(KNS_SIG_BYTES)) return "KNS";
            if (signature.SequenceEqual(AT3_SIG_BYTES)) return "AT3";
            if (signature.SequenceEqual(KTAC_SIG_BYTES)) return "KTAC";
            return "未知";
        }
        private string GetExtension(byte[] signature)
        {
            if (signature.SequenceEqual(KVS_SIG_BYTES)) return ".kvs";
            if (signature.SequenceEqual(KNS_SIG_BYTES)) return ".kns";
            if (signature.SequenceEqual(AT3_SIG_BYTES)) return ".at3";
            if (signature.SequenceEqual(KTAC_SIG_BYTES)) return ".ktac";
            return ".bin";
        }
    }
}
