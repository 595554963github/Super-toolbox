namespace super_toolbox
{
    public class Bwav_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private static readonly byte[] BWAV_SIG_BYTES = { 0x42, 0x57, 0x41, 0x56 };
        private static readonly byte[] AMTA_SIG_BYTES = { 0x41, 0x4D, 0x54, 0x41 };

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

            var barsFiles = Directory.EnumerateFiles(directoryPath, "*.bars", SearchOption.AllDirectories);

            TotalFilesToExtract = barsFiles.Count();
            int processedFiles = 0;

            foreach (var barsFile in barsFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(barsFile)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(barsFile, cancellationToken);

                    List<long> bwavOffsets = new List<long>();
                    List<string> audioNames = new List<string>();

                    for (long i = 0; i < content.Length - 4; i++)
                    {
                        if (i + 3 >= content.Length) break;

                        if (content[i] == BWAV_SIG_BYTES[0] &&
                            content[i + 1] == BWAV_SIG_BYTES[1] &&
                            content[i + 2] == BWAV_SIG_BYTES[2] &&
                            content[i + 3] == BWAV_SIG_BYTES[3])
                        {
                            bwavOffsets.Add(i);
                        }

                        if (content[i] == AMTA_SIG_BYTES[0] &&
                            content[i + 1] == AMTA_SIG_BYTES[1] &&
                            content[i + 2] == AMTA_SIG_BYTES[2] &&
                            content[i + 3] == AMTA_SIG_BYTES[3])
                        {
                            long nameStart = i + 0x48;
                            if (nameStart < content.Length && content[nameStart] == 0x01)
                            {
                                nameStart += 0x8;
                            }

                            long nameLen = 0;
                            for (long j = nameStart; j < content.Length; j++, nameLen++)
                            {
                                if (content[j] == 0xC2)
                                {
                                    nameStart = j + 1;
                                    nameLen = -1;
                                }
                                if (content[j] == 0x00) break;
                            }

                            if (nameLen > 0 && nameStart + nameLen <= content.Length)
                            {
                                string name = System.Text.Encoding.UTF8.GetString(content, (int)nameStart, (int)nameLen);

                                if (audioNames.Contains(name))
                                {
                                    int repeatCounter = 1;
                                    while (audioNames.Contains(name + "-" + repeatCounter.ToString()))
                                    {
                                        repeatCounter++;
                                    }
                                    name = name + "-" + repeatCounter.ToString();
                                }
                                audioNames.Add(name);
                            }
                        }
                    }

                    if (audioNames.Count < bwavOffsets.Count)
                    {
                        for (int i = audioNames.Count; i < bwavOffsets.Count; i++)
                        {
                            audioNames.Add("extra_" + (i + 1).ToString());
                        }
                    }

                    string barsFileName = Path.GetFileNameWithoutExtension(barsFile);
                    string outputSubDir = Path.Combine(directoryPath, barsFileName);
                    Directory.CreateDirectory(outputSubDir);

                    for (int i = 0; i < bwavOffsets.Count; i++)
                    {
                        long offset = bwavOffsets[i];
                        long nextOffset = (i + 1 < bwavOffsets.Count) ? bwavOffsets[i + 1] : content.Length;
                        int length = (int)(nextOffset - offset);

                        if (length <= 0) continue;

                        byte[] bwavData = new byte[length];
                        Array.Copy(content, offset, bwavData, 0, length);

                        string fileName = audioNames[i] + ".bwav";
                        string outputPath = Path.Combine(outputSubDir, fileName);
                        outputPath = GetUniqueFilePath(outputPath);

                        await File.WriteAllBytesAsync(outputPath, bwavData, cancellationToken);

                        if (!extractedFiles.Contains(outputPath))
                        {
                            extractedFiles.Add(outputPath);
                            OnFileExtracted(outputPath);
                            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{barsFile}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{barsFile}时出错:{e.Message}");
                }

                processedFiles++;
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个BWAV文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到BWAV文件");
            }
            OnExtractionCompleted();
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return filePath;
            }

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);

            int duplicateCount = 1;
            string newFilePath;
            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }
    }
}