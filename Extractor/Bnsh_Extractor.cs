namespace super_toolbox
{
    public class Bnsh_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private readonly byte[] BnshSignature = { 0x42, 0x4E, 0x53, 0x48 };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var allFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories).ToList();
            TotalFilesToExtract = allFiles.Count;
            int successCount = 0;
            int totalExtracted = 0;

            try
            {
                foreach (var filePath in allFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");

                    try
                    {
                        byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

                        if (fileBytes.Length < 4)
                        {
                            continue;
                        }

                        List<int> positions = new List<int>();
                        for (int i = 0; i <= fileBytes.Length - 4; i++)
                        {
                            if (fileBytes[i] == BnshSignature[0] &&
                                fileBytes[i + 1] == BnshSignature[1] &&
                                fileBytes[i + 2] == BnshSignature[2] &&
                                fileBytes[i + 3] == BnshSignature[3])
                            {
                                positions.Add(i);
                            }
                        }

                        if (positions.Count == 0)
                        {
                            continue;
                        }

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                        string outputDirectory = Path.Combine(fileDirectory, fileName);
                        Directory.CreateDirectory(outputDirectory);

                        int fileCount = 0;
                        int fileExtractedCount = 0;

                        foreach (int pos in positions)
                        {
                            if (pos + 0x20 > fileBytes.Length)
                            {
                                continue;
                            }

                            int sizeOffset = pos + 0x1C;
                            if (sizeOffset + 4 > fileBytes.Length)
                            {
                                continue;
                            }

                            uint dataSize = BitConverter.ToUInt32(fileBytes, sizeOffset);

                            if (pos + dataSize > fileBytes.Length)
                            {
                                continue;
                            }

                            byte[] extractedData = new byte[dataSize];
                            Array.Copy(fileBytes, pos, extractedData, 0, dataSize);

                            string outputPath = Path.Combine(outputDirectory, $"{fileName}_{fileCount + 1}.bnsh");
                            await File.WriteAllBytesAsync(outputPath, extractedData, cancellationToken);

                            successCount++;
                            fileExtractedCount++;
                            totalExtracted++;
                            extractedFiles.Add(outputPath);
                            OnFileExtracted(outputPath);
                            ExtractionProgress?.Invoke(this, $"提取成功:{Path.GetFileName(outputPath)}");
                            fileCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"{Path.GetFileName(filePath)}处理失败:{ex.Message}");
                        OnExtractionFailed($"{Path.GetFileName(filePath)}处理失败");
                    }
                }

                if (totalExtracted > 0)
                {
                    ExtractionProgress?.Invoke(this, $"提取完成,成功提取{totalExtracted}个BNSH文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成,但未找到任何BNSH文件");
                }
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}