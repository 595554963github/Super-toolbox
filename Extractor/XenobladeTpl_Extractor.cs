using System.Text;

namespace super_toolbox
{
    public class XenobladeTpl_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] CRAM_SIGNATURE = { 0x63, 0x72, 0x61, 0x6D };

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
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);

                        if (IsCramFile(content))
                        {
                            int count = await ExtractCramFilesAsync(content, filePath, extractedDir, extractedFiles, cancellationToken);
                            if (count > 0)
                            {
                                ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中提取出{count}个文件");
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
                        OnExtractionFailed($"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到CRAM文件");
                }

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private bool IsCramFile(byte[] content)
        {
            if (content.Length < 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (content[i] != CRAM_SIGNATURE[i])
                    return false;
            }

            return true;
        }

        private async Task<int> ExtractCramFilesAsync(byte[] content, string filePath, string extractedDir,
            List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int count = 0;

            try
            {
                using (MemoryStream ms = new MemoryStream(content))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    reader.ReadBytes(4);
                    int fileCount = reader.ReadInt32();
                    uint dummy = reader.ReadUInt32();
                    long namesOffset = reader.ReadInt32();
                    var fileInfos = new List<CramFileInfo>();
                    for (int i = 0; i < fileCount; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        uint someCrc = reader.ReadUInt32();
                        string type = new string(reader.ReadChars(4));
                        long offset = reader.ReadInt32();
                        long size = reader.ReadInt32();
                        fileInfos.Add(new CramFileInfo
                        {
                            Crc = someCrc,
                            Type = type,
                            Offset = offset,
                            Size = size
                        });
                    }
                    var names = new List<string>();
                    long currentPos = reader.BaseStream.Position;

                    for (int i = 0; i < fileCount; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        long nameOffset = reader.ReadInt32();
                        long tempPos = reader.BaseStream.Position;
                        reader.BaseStream.Seek(namesOffset + nameOffset, SeekOrigin.Begin);
                        string name = ReadNullTerminatedString(reader);
                        names.Add(name);
                        reader.BaseStream.Seek(tempPos, SeekOrigin.Begin);
                    }

                    string timgDir = Path.Combine(extractedDir, "timg");
                    Directory.CreateDirectory(timgDir);

                    for (int i = 0; i < fileCount; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        var fileInfo = fileInfos[i];
                        string originalName = names[i];

                        if (string.IsNullOrEmpty(originalName))
                        {
                            originalName = $"file_{i}";
                        }

                        string fileName = Path.GetFileName(originalName);
                        string cleanFileName = CleanFileName(fileName);

                        string finalFilePath = Path.Combine(timgDir, cleanFileName);
                        finalFilePath = await GenerateUniqueFilePathAsync(finalFilePath, cancellationToken);

                        if (fileInfo.Offset >= 0 && fileInfo.Size > 0 &&
                            fileInfo.Offset + fileInfo.Size <= content.Length)
                        {
                            byte[] fileData = new byte[fileInfo.Size];
                            Array.Copy(content, fileInfo.Offset, fileData, 0, fileInfo.Size);

                            try
                            {
                                await File.WriteAllBytesAsync(finalFilePath, fileData, cancellationToken);
                                extractedFiles.Add(finalFilePath);
                                OnFileExtracted(finalFilePath);
                                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(finalFilePath)}");
                                count++;
                            }
                            catch (Exception ex)
                            {
                                ExtractionError?.Invoke(this, $"写入文件{finalFilePath}时出错:{ex.Message}");
                                OnExtractionFailed($"写入文件{finalFilePath}时出错:{ex.Message}");
                            }
                        }
                        else
                        {
                            ExtractionError?.Invoke(this, $"文件{originalName}的偏移或大小无效");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解析CRAM文件时出错:{ex.Message}");
                OnExtractionFailed($"解析CRAM文件时出错:{ex.Message}");
            }

            return count;
        }

        private string ReadNullTerminatedString(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            byte b;

            while ((b = reader.ReadByte()) != 0)
            {
                bytes.Add(b);
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private string CleanFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c.ToString(), "_");
            }

            return fileName;
        }

        private async Task<string> GenerateUniqueFilePathAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
                counter++;
                ThrowIfCancellationRequested(cancellationToken);
            }
            while (File.Exists(newPath));
            return newPath;
        }

        private class CramFileInfo
        {
            public uint Crc { get; set; }
            public string Type { get; set; } = string.Empty;
            public long Offset { get; set; }
            public long Size { get; set; }
        }
    }
}