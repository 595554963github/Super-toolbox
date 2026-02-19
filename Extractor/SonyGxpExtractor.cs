namespace super_toolbox
{
    public class SonyGxpExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] GXP_HEADER = { 0x47, 0x58, 0x50, 0x00, 0x01, 0x05, 0x50, 0x03 };

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

        private int ReadLittleEndianInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
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

            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);
            TotalFilesToExtract = files.Count();
            int processedFiles = 0;

            foreach (var file in files)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(file)}");

                try
                {
                    string outputDir = Path.Combine(Path.GetDirectoryName(file) ?? directoryPath,
                                                   $"{Path.GetFileNameWithoutExtension(file)}");
                    Directory.CreateDirectory(outputDir);

                    byte[] content = await File.ReadAllBytesAsync(file, cancellationToken);
                    string fileNamePrefix = Path.GetFileNameWithoutExtension(file);
                    int count = 1;
                    int index = 0;

                    while (index <= content.Length - GXP_HEADER.Length)
                    {
                        int startIndex = IndexOf(content, GXP_HEADER, index);
                        if (startIndex == -1) break;

                        int sizeOffsetPos = startIndex + 0x08;
                        if (sizeOffsetPos + 4 > content.Length)
                        {
                            index = startIndex + 1;
                            continue;
                        }

                        int fileSize = ReadLittleEndianInt32(content, sizeOffsetPos);

                        if (fileSize <= 0 || startIndex + fileSize > content.Length)
                        {
                            index = startIndex + 1;
                            continue;
                        }

                        byte[] fileData = new byte[fileSize];
                        Array.Copy(content, startIndex, fileData, 0, fileSize);

                        string fileName = $"{fileNamePrefix}_{count}.gxp";
                        string outputPath = Path.Combine(outputDir, fileName);
                        outputPath = GetUniqueFilePath(outputPath);

                        await File.WriteAllBytesAsync(outputPath, fileData, cancellationToken);
                        extractedFiles.Add(outputPath);
                        OnFileExtracted(outputPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");

                        count++;
                        index = startIndex + fileSize;
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
                    ExtractionError?.Invoke(this, $"处理文件{file}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{file}时出错:{e.Message}");
                }

                processedFiles++;
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个gxp文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到gxp文件");
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