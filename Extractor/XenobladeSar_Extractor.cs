namespace super_toolbox
{
    public class XenobladeSar_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const uint SAR1_LITTLE_ENDIAN = 0x53415231;
        private const uint SAR1_BIG_ENDIAN = 0x31524153;  

        private struct SarFileEntry
        {
            public uint Offset;
            public uint Length;
            public string FileName;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            var files = Directory.GetFiles(directoryPath, "*.sar", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            TotalFilesToExtract = files.Length;

            ExtractionStarted?.Invoke(this, $"开始处理{TotalFilesToExtract}个sar文件");

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(file)}");
                    await ProcessSarFileAsync(file, extractedDir, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{file}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件{file}时出错:{ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"提取完成:提取了{ExtractedFileCount}个文件");
            OnExtractionCompleted();
        }

        private async Task ProcessSarFileAsync(string filePath, string destinationFolder, CancellationToken cancellationToken)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fileStream);

            uint fourCC = reader.ReadUInt32();

            if (fourCC != SAR1_LITTLE_ENDIAN && fourCC != SAR1_BIG_ENDIAN)
            {
                ExtractionError?.Invoke(this, $"文件{Path.GetFileName(filePath)}不是有效的SAR文件");
                return;
            }

            bool isBigEndian = fourCC == SAR1_BIG_ENDIAN;
            reader.BaseStream.Position = 0;

            if (isBigEndian)
            {
                reader.BaseStream.Position = 0;
                fourCC = ReadUInt32BigEndian(reader);

                uint fileSize = ReadUInt32BigEndian(reader);
                uint version = ReadUInt32BigEndian(reader);
                uint fileCount = ReadUInt32BigEndian(reader);
                uint offsetTable = ReadUInt32BigEndian(reader);
                uint dataOffset = ReadUInt32BigEndian(reader);
                uint dummy1 = ReadUInt32BigEndian(reader);
                uint dummy2 = ReadUInt32BigEndian(reader);

                byte[] pathBytes = reader.ReadBytes(128);
                string basePath = System.Text.Encoding.ASCII.GetString(pathBytes).TrimEnd('\0');

                reader.BaseStream.Position = offsetTable;

                var fileEntries = new List<SarFileEntry>();

                for (int i = 0; i < fileCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint entryOffset = ReadUInt32BigEndian(reader);
                    uint entryLength = ReadUInt32BigEndian(reader);
                    uint entryDummy = ReadUInt32BigEndian(reader);

                    string fileName;
                    if (version > 500)
                    {
                        byte[] nameBytes = reader.ReadBytes(36);
                        fileName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                        reader.BaseStream.Position += 48;
                    }
                    else
                    {
                        byte[] nameBytes = reader.ReadBytes(52);
                        fileName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                    }

                    string fullPath = string.IsNullOrEmpty(basePath) ? fileName : Path.Combine(basePath, fileName);

                    fileEntries.Add(new SarFileEntry
                    {
                        Offset = entryOffset,
                        Length = entryLength,
                        FileName = fullPath
                    });
                }

                await ExtractFilesAsync(reader, fileEntries, destinationFolder, Path.GetFileNameWithoutExtension(filePath), cancellationToken);
            }
            else
            {
                reader.BaseStream.Position = 0;
                fourCC = reader.ReadUInt32();
                uint fileSize = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint fileCount = reader.ReadUInt32();
                uint offsetTable = reader.ReadUInt32();
                uint dataOffset = reader.ReadUInt32();
                uint dummy1 = reader.ReadUInt32();
                uint dummy2 = reader.ReadUInt32();

                byte[] pathBytes = reader.ReadBytes(128);
                string basePath = System.Text.Encoding.ASCII.GetString(pathBytes).TrimEnd('\0');

                reader.BaseStream.Position = offsetTable;

                var fileEntries = new List<SarFileEntry>();

                for (int i = 0; i < fileCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint entryOffset = reader.ReadUInt32();
                    uint entryLength = reader.ReadUInt32();
                    uint entryDummy = reader.ReadUInt32();

                    string fileName;
                    if (version > 500)
                    {
                        byte[] nameBytes = reader.ReadBytes(36);
                        fileName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                        reader.BaseStream.Position += 48;
                    }
                    else
                    {
                        byte[] nameBytes = reader.ReadBytes(52);
                        fileName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                    }

                    string fullPath = string.IsNullOrEmpty(basePath) ? fileName : Path.Combine(basePath, fileName);

                    fileEntries.Add(new SarFileEntry
                    {
                        Offset = entryOffset,
                        Length = entryLength,
                        FileName = fullPath
                    });
                }

                await ExtractFilesAsync(reader, fileEntries, destinationFolder, Path.GetFileNameWithoutExtension(filePath), cancellationToken);
            }
        }

        private async Task ExtractFilesAsync(BinaryReader reader, List<SarFileEntry> fileEntries, string destinationFolder, string baseFileName, CancellationToken cancellationToken)
        {
            foreach (var entry in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    reader.BaseStream.Position = entry.Offset;

                    byte[] fileData = reader.ReadBytes((int)entry.Length);

                    string fullDestinationPath = Path.Combine(destinationFolder, baseFileName);
                    string entryDirectory = Path.GetDirectoryName(entry.FileName) ?? "";
                    string targetDirectory = Path.Combine(fullDestinationPath, entryDirectory);

                    Directory.CreateDirectory(targetDirectory);

                    string fileName = Path.GetFileName(entry.FileName);
                    fileName = CleanFileName(fileName);

                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = $"file_{entry.Offset:X8}.bin";
                    }

                    string filePath = Path.Combine(targetDirectory, fileName);

                    await File.WriteAllBytesAsync(filePath, fileData, cancellationToken);

                    OnFileExtracted(filePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"提取文件{entry.FileName}时出错:{ex.Message}");
                }
            }
        }

        private uint ReadUInt32BigEndian(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt32(bytes, 0);
        }

        private string CleanFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c.ToString(), "_");
            }

            fileName = fileName.Trim().Trim('.');

            return fileName;
        }
    }
}