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

            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            TotalFilesToExtract = files.Length;

            ExtractionStarted?.Invoke(this, $"开始处理{TotalFilesToExtract}个文件");

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
            reader.BaseStream.Position = 0;

            if (fourCC != SAR1_LITTLE_ENDIAN && fourCC != SAR1_BIG_ENDIAN)
            {
                ExtractionError?.Invoke(this, $"文件{Path.GetFileName(filePath)}不是有效的SAR文件");
                return;
            }

            bool isBigEndian = fourCC == SAR1_BIG_ENDIAN;

            uint fileSize;
            uint version;
            uint fileCount;
            uint offsetTable;
            uint dataOffset;
            uint dummy1, dummy2;
            string basePath;

            if (isBigEndian)
            {
                fourCC = ReadUInt32BigEndian(reader);
                fileSize = ReadUInt32BigEndian(reader);
                version = ReadUInt32BigEndian(reader);
                fileCount = ReadUInt32BigEndian(reader);
                offsetTable = ReadUInt32BigEndian(reader);
                dataOffset = ReadUInt32BigEndian(reader);
                dummy1 = ReadUInt32BigEndian(reader);
                dummy2 = ReadUInt32BigEndian(reader);

                byte[] pathBytes = reader.ReadBytes(128);
                basePath = System.Text.Encoding.ASCII.GetString(pathBytes).TrimEnd('\0');

                ExtractionProgress?.Invoke(this, $"大端序SAR文件:版本={version},文件数={fileCount},偏移表=0x{offsetTable:X8}");
                ExtractionProgress?.Invoke(this, $"基础路径:{basePath}");
            }
            else
            {
                fourCC = reader.ReadUInt32();
                fileSize = reader.ReadUInt32();
                version = reader.ReadUInt32();
                fileCount = reader.ReadUInt32();
                offsetTable = reader.ReadUInt32();
                dataOffset = reader.ReadUInt32();
                dummy1 = reader.ReadUInt32();
                dummy2 = reader.ReadUInt32();

                byte[] pathBytes = reader.ReadBytes(128);
                basePath = System.Text.Encoding.ASCII.GetString(pathBytes).TrimEnd('\0');
            }

            reader.BaseStream.Position = offsetTable;

            var fileEntries = new List<SarFileEntry>();

            for (int i = 0; i < fileCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint entryOffset;
                uint entryLength;
                uint entryDummy;

                if (isBigEndian)
                {
                    entryOffset = ReadUInt32BigEndian(reader);
                    entryLength = ReadUInt32BigEndian(reader);
                    entryDummy = ReadUInt32BigEndian(reader);
                }
                else
                {
                    entryOffset = reader.ReadUInt32();
                    entryLength = reader.ReadUInt32();
                    entryDummy = reader.ReadUInt32();
                }

                string fileName;
                if (version > 500)
                {
                    byte[] nameBytes = reader.ReadBytes(36);
                    fileName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                    reader.BaseStream.Position += 36;
                }
                else
                {
                    byte[] nameBytes = reader.ReadBytes(52);
                    fileName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                }

                string fullPath;
                if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fileName))
                {
                    fullPath = string.IsNullOrEmpty(fileName) ? $"file_{i:D4}.bin" : fileName;
                }
                else
                {
                    string normalizedBasePath = basePath;
                    if (normalizedBasePath.Length > 2 && normalizedBasePath[1] == ':')
                    {
                        normalizedBasePath = normalizedBasePath.Substring(2).TrimStart('\\', '/');
                    }

                    fullPath = Path.Combine(normalizedBasePath, fileName).Replace('\\', '/');
                }

                fileEntries.Add(new SarFileEntry
                {
                    Offset = entryOffset,
                    Length = entryLength,
                    FileName = fullPath
                });
            }

            await ExtractFilesAsync(reader, fileEntries, destinationFolder, Path.GetFileNameWithoutExtension(filePath), cancellationToken);
        }

        private async Task ExtractFilesAsync(BinaryReader reader, List<SarFileEntry> fileEntries, string destinationFolder, string baseFileName, CancellationToken cancellationToken)
        {
            string baseExtractFolder = Path.Combine(destinationFolder, baseFileName);

            foreach (var entry in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (reader.BaseStream.Position != entry.Offset)
                    {
                        reader.BaseStream.Position = entry.Offset;
                    }

                    byte[] fileData = reader.ReadBytes((int)entry.Length);

                    string fullExtractPath;

                    if (string.IsNullOrEmpty(entry.FileName))
                    {
                        fullExtractPath = Path.Combine(baseExtractFolder, $"file_{entry.Offset:X8}.bin");
                    }
                    else
                    {
                        string normalizedPath = entry.FileName.Replace('/', Path.DirectorySeparatorChar)
                                                             .Replace('\\', Path.DirectorySeparatorChar);

                        if (Path.IsPathRooted(normalizedPath))
                        {
                            normalizedPath = normalizedPath.TrimStart(Path.DirectorySeparatorChar);
                        }

                        fullExtractPath = Path.Combine(baseExtractFolder, normalizedPath);
                    }

                    string? fileDirectory = Path.GetDirectoryName(fullExtractPath);
                    if (!string.IsNullOrEmpty(fileDirectory))
                    {
                        Directory.CreateDirectory(fileDirectory);
                    }

                    string fileName = Path.GetFileName(fullExtractPath);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = $"file_{entry.Offset:X8}.bin";
                    }

                    fileName = CleanFileName(fileName);

                    string finalFilePath = Path.Combine(fileDirectory ?? baseExtractFolder, fileName);

                    await File.WriteAllBytesAsync(finalFilePath, fileData, cancellationToken);

                    OnFileExtracted(finalFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(finalFilePath)}");
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
