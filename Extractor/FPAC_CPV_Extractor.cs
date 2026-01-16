namespace super_toolbox
{
    public class FPAC_CPV_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] HIP_SIGNATURE = { 0x48, 0x49, 0x50, 0x00, 0x25, 0x01, 0x00, 0x00 };
        private static readonly byte[] LIP_SIGNATURE = { 0x4C, 0x49, 0x50, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] GXT_SIGNATURE = { 0x47, 0x58, 0x54, 0x00, 0x03, 0x00, 0x00, 0x10 };
        private static readonly byte[] RIFF_SIGNATURE = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] AT9_SIGNATURE = { 0x34, 0x00, 0x00, 0x00, 0xFE, 0xFF, 0x01, 0x00 };

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
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase));
            TotalFilesToExtract = 0;

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);

                    Dictionary<int, string> foundEntries = new Dictionary<int, string>();

                    FindAllEntries(content, HIP_SIGNATURE, "hip", foundEntries);
                    FindAllEntries(content, LIP_SIGNATURE, "lip", foundEntries);
                    FindAllEntries(content, GXT_SIGNATURE, "gxt", foundEntries);
                    FindRiffEntries(content, foundEntries);

                    var sortedEntries = foundEntries.OrderBy(x => x.Key).ToList();

                    if (sortedEntries.Count == 0)
                    {
                        continue;
                    }
                    else if (sortedEntries.Count == 1)
                    {
                        ProcessSingleEntry(content, sortedEntries[0].Key, sortedEntries[0].Value,
                                         filePath, extractedDir, extractedFiles);
                    }
                    else
                    {
                        ProcessMultipleEntries(content, sortedEntries, filePath, extractedDir, extractedFiles);
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
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到相关文件");
            }
            OnExtractionCompleted();
        }

        private void FindAllEntries(byte[] content, byte[] signature, string extension, Dictionary<int, string> entries)
        {
            int index = 0;
            while (true)
            {
                int foundIndex = IndexOf(content, signature, index);
                if (foundIndex == -1) break;

                if (!entries.ContainsKey(foundIndex))
                {
                    entries.Add(foundIndex, extension);
                }

                index = foundIndex + 1;
            }
        }

        private void FindRiffEntries(byte[] content, Dictionary<int, string> entries)
        {
            int index = 0;
            while (true)
            {
                int riffIndex = IndexOf(content, RIFF_SIGNATURE, index);
                if (riffIndex == -1) break;

                if (riffIndex + 0x17 < content.Length)
                {
                    bool isAt9 = true;
                    for (int i = 0; i < AT9_SIGNATURE.Length; i++)
                    {
                        if (content[riffIndex + 0x10 + i] != AT9_SIGNATURE[i])
                        {
                            isAt9 = false;
                            break;
                        }
                    }

                    if (isAt9 && !entries.ContainsKey(riffIndex))
                    {
                        entries.Add(riffIndex, "at9");
                    }
                }

                index = riffIndex + 1;
            }
        }

        private void ProcessSingleEntry(byte[] content, int startIndex, string extension,
                                       string filePath, string extractedDir, List<string> extractedFiles)
        {
            int dataLength = content.Length - startIndex;
            byte[] fileData = new byte[dataLength];
            Array.Copy(content, startIndex, fileData, 0, dataLength);

            if (extension == "at9")
            {
                if (startIndex + 8 < content.Length)
                {
                    int riffSize = BitConverter.ToInt32(content, startIndex + 4) + 8;
                    if (startIndex + riffSize <= content.Length)
                    {
                        fileData = new byte[riffSize];
                        Array.Copy(content, startIndex, fileData, 0, riffSize);
                    }
                }
            }

            SaveFileData(fileData, filePath, extension, extractedDir, extractedFiles, 1);
        }

        private void ProcessMultipleEntries(byte[] content, List<KeyValuePair<int, string>> entries,
                                          string filePath, string extractedDir, List<string> extractedFiles)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                int startIndex = entries[i].Key;
                string extension = entries[i].Value;

                int endIndex = (i + 1 < entries.Count) ? entries[i + 1].Key : content.Length;
                int dataLength = endIndex - startIndex;

                byte[] fileData = new byte[dataLength];
                Array.Copy(content, startIndex, fileData, 0, dataLength);

                if (extension == "at9")
                {
                    if (startIndex + 8 < content.Length)
                    {
                        int riffSize = BitConverter.ToInt32(content, startIndex + 4) + 8;
                        if (riffSize <= dataLength)
                        {
                            fileData = new byte[riffSize];
                            Array.Copy(content, startIndex, fileData, 0, riffSize);
                        }
                    }
                }

                SaveFileData(fileData, filePath, extension, extractedDir, extractedFiles, i + 1);
            }
        }

        private void SaveFileData(byte[] fileData, string sourceFilePath, string extension,
                                 string extractedDir, List<string> extractedFiles, int count)
        {
            if (fileData.Length <= 0) return;

            string baseFileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string outputFileName = $"{baseFileName}_{count}.{extension}";
            string outputFilePath = Path.Combine(extractedDir, outputFileName);

            if (File.Exists(outputFilePath))
            {
                int duplicateCount = 1;
                do
                {
                    outputFileName = $"{baseFileName}_{count}_dup{duplicateCount}.{extension}";
                    outputFilePath = Path.Combine(extractedDir, outputFileName);
                    duplicateCount++;
                } while (File.Exists(outputFilePath));
            }

            try
            {
                File.WriteAllBytes(outputFilePath, fileData);
                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{outputFileName}");
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