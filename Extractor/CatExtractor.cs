using System.Text;

namespace super_toolbox
{
    public class CatExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

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

            var catFiles = Directory.EnumerateFiles(directoryPath, "*.cat", SearchOption.AllDirectories);

            TotalFilesToExtract = catFiles.Count();
            int processedFiles = 0;

            foreach (var catFile in catFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(catFile)}");

                try
                {
                    string outputDir = Path.Combine(Path.GetDirectoryName(catFile) ?? directoryPath,
                                                   $"{Path.GetFileNameWithoutExtension(catFile)}");
                    Directory.CreateDirectory(outputDir);

                    byte[] content = await File.ReadAllBytesAsync(catFile, cancellationToken);
                    string catFileNamePrefix = Path.GetFileNameWithoutExtension(catFile);
                    await ProcessCatFile(content, catFileNamePrefix, outputDir, extractedFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{catFile}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{catFile}时出错:{e.Message}");
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

        private async Task ProcessCatFile(byte[] content, string catFileNamePrefix, string outputDir,
                                         List<string> extractedFiles, CancellationToken cancellationToken)
        {
            using (MemoryStream ms = new MemoryStream(content))
            using (BinaryReader br = new BinaryReader(ms))
            {
                uint ver = br.ReadUInt32();

                if (ver >= 0x10)
                {
                    ms.Position = 0;
                    int fileIndex = 1;
                    int totalValidFiles = 0;

                    while (ms.Position < ms.Length - 12)
                    {
                        uint offset = br.ReadUInt32();
                        if (offset == 0xFFFFFFFF || offset == 0)
                            break;

                        uint size = br.ReadUInt32();
                        br.ReadUInt32();

                        if (size > 0 && offset + size <= content.Length)
                        {
                            totalValidFiles++;
                        }
                    }

                    ms.Position = 0;
                    fileIndex = 1;

                    while (ms.Position < ms.Length - 12)
                    {
                        uint offset = br.ReadUInt32();
                        if (offset == 0xFFFFFFFF || offset == 0)
                            break;

                        uint size = br.ReadUInt32();
                        br.ReadUInt32();

                        if (size > 0 && offset + size <= content.Length)
                        {
                            byte[] fileData = new byte[size];
                            Array.Copy(content, offset, fileData, 0, size);

                            string extension = GetExtensionFromHeader(fileData);
                            string fileName = totalValidFiles == 1 ?
                                $"{catFileNamePrefix}.{extension}" :
                                $"{catFileNamePrefix}_{fileIndex}.{extension}";

                            string outputPath = Path.Combine(outputDir, fileName);
                            outputPath = GetUniqueFilePath(outputPath);

                            await File.WriteAllBytesAsync(outputPath, fileData, cancellationToken);
                            extractedFiles.Add(outputPath);
                            OnFileExtracted(outputPath);
                            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");

                            fileIndex++;
                        }
                    }
                }
                else
                {
                    br.ReadUInt32();
                    br.ReadUInt32();
                    uint infoOff = br.ReadUInt32();
                    uint dataSize = br.ReadUInt32();
                    br.ReadUInt32();
                    uint files = br.ReadUInt32();

                    if (infoOff >= content.Length)
                    {
                        throw new InvalidDataException("无效的信息表偏移量");
                    }

                    ms.Position = infoOff;
                    br.ReadUInt32();
                    files = br.ReadUInt32();
                    br.ReadUInt32();
                    br.ReadUInt32();
                    br.ReadUInt32();

                    uint[] offsets = new uint[files];
                    for (int i = 0; i < files; i++)
                    {
                        offsets[i] = br.ReadUInt32() + infoOff;
                    }

                    uint[] sizes = new uint[files];
                    for (int i = 0; i < files; i++)
                    {
                        sizes[i] = br.ReadUInt32();
                    }

                    if (ver < 3)
                    {
                        ms.Position = dataSize + infoOff;
                        for (int i = 0; i < files; i++)
                        {
                            br.ReadBytes(0x20);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < files; i++)
                        {
                            br.ReadUInt32();
                        }
                    }

                    int totalValidFiles = 0;
                    for (int i = 0; i < files; i++)
                    {
                        if (sizes[i] > 0 && offsets[i] + sizes[i] <= content.Length)
                        {
                            totalValidFiles++;
                        }
                    }

                    for (int i = 0; i < files; i++)
                    {
                        if (sizes[i] > 0 && offsets[i] + sizes[i] <= content.Length)
                        {
                            byte[] fileData = new byte[sizes[i]];
                            Array.Copy(content, offsets[i], fileData, 0, sizes[i]);

                            string extension = GetExtensionFromHeader(fileData);
                            string fileName = totalValidFiles == 1 ?
                                $"{catFileNamePrefix}.{extension}" :
                                $"{catFileNamePrefix}_{i + 1}.{extension}";

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
        }
        private string GetExtensionFromHeader(byte[] fileData)
        {
            if (fileData == null || fileData.Length < 4)
                return "dat";

            StringBuilder headerBuilder = new StringBuilder();
            for (int i = 0; i < 4 && i < fileData.Length; i++)
            {
                byte b = fileData[i];
                if (b >= 32 && b <= 126)
                {
                    headerBuilder.Append((char)b);
                }
                else
                {
                    return "dat";
                }
            }

            string header = headerBuilder.ToString().Trim();
            if (string.IsNullOrEmpty(header) || header.Length < 2)
                return "dat";
            string cleanHeader = new string(header.Where(c => char.IsLetterOrDigit(c)).ToArray());
            if (string.IsNullOrEmpty(cleanHeader) || cleanHeader.Length < 2)
                return "dat";

            return cleanHeader.ToLower();
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
