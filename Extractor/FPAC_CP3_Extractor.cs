namespace super_toolbox
{
    public class FPAC_CP3_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] HIP_START_MARKER = { 0x48, 0x49, 0x50, 0x00 };
        private VagExtractor vagExtractor = new VagExtractor();

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

        private List<int> FindAllHipPositions(byte[] data)
        {
            List<int> positions = new List<int>();
            int index = 0;

            while (index < data.Length)
            {
                int pos = IndexOf(data, HIP_START_MARKER, index);
                if (pos == -1)
                    break;

                positions.Add(pos);
                index = pos + 1;
            }

            return positions;
        }

        private int GetCorrectSize(byte[] data, int startIndex)
        {
            if (startIndex + 12 > data.Length)
                return -1;

            int littleEndianSize = BitConverter.ToInt32(data, startIndex + 8);
            int bigEndianSize = (data[startIndex + 8] << 24) | (data[startIndex + 9] << 16) |
                                (data[startIndex + 10] << 8) | data[startIndex + 11];

            int maxSize = data.Length - startIndex;

            if (littleEndianSize > 0 && littleEndianSize <= maxSize)
            {
                return littleEndianSize;
            }
            else if (bigEndianSize > 0 && bigEndianSize <= maxSize)
            {
                return bigEndianSize;
            }

            return -1;
        }

        private byte[] TrimHipFile(byte[] data, int startIndex)
        {
            int correctSize = GetCorrectSize(data, startIndex);

            if (correctSize > 0 && startIndex + correctSize <= data.Length)
            {
                byte[] trimmedData = new byte[correctSize];
                Array.Copy(data, startIndex, trimmedData, 0, correctSize);
                return trimmedData;
            }

            return data;
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

            string sourceFolderName = new DirectoryInfo(directoryPath).Name;
            string parentDirectory = Directory.GetParent(directoryPath)?.FullName ?? directoryPath;
            string extractedFolder = Path.Combine(parentDirectory, sourceFolderName, "Extracted");
            Directory.CreateDirectory(extractedFolder);

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*pac", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;
            int hipCounter = 0;
            List<string> tempHipFiles = new List<string>();

            vagExtractor.ExtractionProgress += (sender, message) =>
            {
                ExtractionProgress?.Invoke(this, message);
            };

            vagExtractor.ExtractionError += (sender, message) =>
            {
                ExtractionError?.Invoke(this, message);
            };

            vagExtractor.FileExtracted += (sender, fileName) =>
            {
                OnFileExtracted(fileName);
            };

            try
            {
                await vagExtractor.ExtractAsync(directoryPath, cancellationToken);

                foreach (var filePath in filePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                        string baseFileName = Path.GetFileNameWithoutExtension(filePath);

                        List<int> hipPositions = FindAllHipPositions(content);

                        for (int i = 0; i < hipPositions.Count; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            int startPos = hipPositions[i];
                            int endPos = (i + 1 < hipPositions.Count) ? hipPositions[i + 1] : content.Length;

                            int length = endPos - startPos;

                            if (length > 0)
                            {
                                hipCounter++;
                                byte[] hipData = new byte[length];
                                Array.Copy(content, startPos, hipData, 0, length);

                                string tempFileName = $"{baseFileName}_{hipCounter}_temp.hip";
                                string tempFilePath = Path.Combine(extractedFolder, tempFileName);

                                await File.WriteAllBytesAsync(tempFilePath, hipData, cancellationToken);
                                tempHipFiles.Add(tempFilePath);
                                ExtractionProgress?.Invoke(this, $"已提取临时hip:{tempFileName}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"开始修剪HIP文件,共{tempHipFiles.Count}个");

                int trimmedCount = 0;
                foreach (var tempFile in tempHipFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        byte[] hipContent = await File.ReadAllBytesAsync(tempFile, cancellationToken);

                        int hipStartIndex = IndexOf(hipContent, HIP_START_MARKER, 0);

                        if (hipStartIndex != -1)
                        {
                            byte[] trimmedData = TrimHipFile(hipContent, hipStartIndex);

                            string finalFileName = Path.GetFileName(tempFile).Replace("_temp.hip", ".hip");
                            string finalFilePath = Path.Combine(extractedFolder, finalFileName);

                            if (File.Exists(finalFilePath))
                            {
                                int duplicateCount = 1;
                                do
                                {
                                    string nameWithoutExt = Path.GetFileNameWithoutExtension(finalFileName);
                                    finalFileName = $"{nameWithoutExt}_dup{duplicateCount}.hip";
                                    finalFilePath = Path.Combine(extractedFolder, finalFileName);
                                    duplicateCount++;
                                } while (File.Exists(finalFilePath));
                            }

                            await File.WriteAllBytesAsync(finalFilePath, trimmedData, cancellationToken);
                            OnFileExtracted(finalFilePath);
                            ExtractionProgress?.Invoke(this, $"已修剪hip:{finalFileName}");
                            trimmedCount++;
                        }

                        File.Delete(tempFile);
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"修剪文件{tempFile}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,共处理{processedFiles}个源文件,提取出{trimmedCount}个HIP文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                foreach (var tempFile in tempHipFiles)
                {
                    try { File.Delete(tempFile); } catch { }
                }
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }
    }
}
