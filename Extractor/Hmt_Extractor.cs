namespace super_toolbox
{
    public class Hmt_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] HMT_SIG_BYTES = { 0x48, 0x4D, 0x4F, 0x54 };
        private static readonly byte[] KFMO_SIG_BYTES = { 0x4B, 0x46, 0x4D, 0x4F };

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

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);
            TotalFilesToExtract = 0;

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    int index = 0;
                    int innerCount = 1;

                    while (index < content.Length)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        int hmtStartIndex = IndexOf(content, HMT_SIG_BYTES, index);
                        if (hmtStartIndex == -1) break;

                        if (hmtStartIndex + 0x20 + 3 < content.Length)
                        {
                            byte[] kfmoCheck = new byte[4];
                            Array.Copy(content, hmtStartIndex + 0x20, kfmoCheck, 0, 4);

                            if (kfmoCheck[0] == KFMO_SIG_BYTES[0] &&
                                kfmoCheck[1] == KFMO_SIG_BYTES[1] &&
                                kfmoCheck[2] == KFMO_SIG_BYTES[2] &&
                                kfmoCheck[3] == KFMO_SIG_BYTES[3])
                            {
                                if (hmtStartIndex + 7 < content.Length)
                                {
                                    int fileSize = BitConverter.ToInt32(content, hmtStartIndex + 4);

                                    if (hmtStartIndex + fileSize <= content.Length)
                                    {
                                        string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                                        string outputDir = Path.Combine(directoryPath, baseFileName);
                                        Directory.CreateDirectory(outputDir);

                                        byte[] hmtData = new byte[fileSize];
                                        Array.Copy(content, hmtStartIndex, hmtData, 0, fileSize);

                                        string outputFileName = $"{baseFileName}_{innerCount}.hmt";
                                        string outputFilePath = Path.Combine(outputDir, outputFileName);

                                        if (File.Exists(outputFilePath))
                                        {
                                            int duplicateCount = 1;
                                            do
                                            {
                                                outputFileName = $"{baseFileName}_{innerCount}_dup{duplicateCount}.hmt";
                                                outputFilePath = Path.Combine(outputDir, outputFileName);
                                                duplicateCount++;
                                            } while (File.Exists(outputFilePath));
                                        }

                                        try
                                        {
                                            File.WriteAllBytes(outputFilePath, hmtData);
                                            if (!extractedFiles.Contains(outputFilePath))
                                            {
                                                extractedFiles.Add(outputFilePath);
                                                OnFileExtracted(outputFilePath);
                                                ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
                                                innerCount++;
                                            }
                                        }
                                        catch (IOException e)
                                        {
                                            ExtractionError?.Invoke(this, $"写入文件{outputFilePath}时出错:{e.Message}");
                                            OnExtractionFailed($"写入文件{outputFilePath}时出错:{e.Message}");
                                        }
                                    }
                                }
                            }
                        }

                        index = hmtStartIndex + 1;
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
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个HMT文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到HMT文件");
            }

            OnExtractionCompleted();
        }
    }
}