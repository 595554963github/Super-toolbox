namespace super_toolbox
{
    public class Ovk_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

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

            var ovkFiles = Directory.EnumerateFiles(directoryPath, "*.ovk", SearchOption.AllDirectories);
            var nwkFiles = Directory.EnumerateFiles(directoryPath, "*.nwk", SearchOption.AllDirectories);
            var allFiles = ovkFiles.Concat(nwkFiles);

            TotalFilesToExtract = allFiles.Count();
            int processedFiles = 0;

            foreach (var file in allFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(file)}");

                try
                {
                    string outputDir = Path.Combine(Path.GetDirectoryName(file) ?? directoryPath,
                                                   $"{Path.GetFileNameWithoutExtension(file)}");
                    Directory.CreateDirectory(outputDir);

                    byte[] content = await File.ReadAllBytesAsync(file, cancellationToken);
                    string filePrefix = Path.GetFileNameWithoutExtension(file);
                    bool isOvk = file.EndsWith(".ovk", StringComparison.OrdinalIgnoreCase);
                    await ProcessOvkFile(content, filePrefix, outputDir, extractedFiles, isOvk, cancellationToken);
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
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到有效文件");
            }
            OnExtractionCompleted();
        }

        private async Task ProcessOvkFile(byte[] content, string filePrefix, string outputDir,
                                 List<string> extractedFiles, bool isOvk, CancellationToken cancellationToken)
        {
            using (MemoryStream ms = new MemoryStream(content))
            using (BinaryReader br = new BinaryReader(ms))
            {
                int count = br.ReadInt32();
                if (!IsSaneCount(count))
                    throw new InvalidDataException("无效的文件数量");

                uint entrySize = isOvk ? 0x10u : 0xCu;
                uint dataOffset = 4u + (uint)count * entrySize;

                if (dataOffset >= content.Length)
                    throw new InvalidDataException("数据偏移超出文件范围");

                string extension = isOvk ? "ogg" : "nwa";
                int validCount = 0;

                for (int i = 0; i < count; i++)
                {
                    long pos = br.BaseStream.Position;
                    uint size = br.ReadUInt32();
                    uint offset = br.ReadUInt32();
                    uint id = br.ReadUInt32();

                    if (isOvk)
                    {
                        br.ReadUInt32();
                    }

                    if (offset >= dataOffset && size > 0 && offset + size <= content.Length)
                    {
                        validCount++;
                    }
                }

                br.BaseStream.Position = 4;

                for (int i = 0; i < count; i++)
                {
                    uint size = br.ReadUInt32();
                    uint offset = br.ReadUInt32();
                    uint id = br.ReadUInt32();

                    if (isOvk)
                    {
                        br.ReadUInt32();
                    }

                    if (offset < dataOffset)
                        continue;

                    if (size > 0 && offset + size <= content.Length)
                    {
                        byte[] fileData = new byte[size];
                        Array.Copy(content, offset, fileData, 0, size);

                        string fileName = validCount == 1 ?
                            $"{filePrefix}.{extension}" :
                            $"{filePrefix}_{i + 1}.{extension}";
                        string outputPath = Path.Combine(outputDir, fileName);
                        outputPath = GetUniqueFilePath(outputPath);

                        await File.WriteAllBytesAsync(outputPath, fileData, cancellationToken);
                        extractedFiles.Add(outputPath);
                        OnFileExtracted(outputPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
                    }
                }
            }
        }

        private bool IsSaneCount(int count)
        {
            return count > 0 && count < 10000;
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