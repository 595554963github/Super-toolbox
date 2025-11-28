using System.ComponentModel;
using System.Text;

namespace super_toolbox
{
    public class PodExtractor : BaseExtractor
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
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            List<string> extractedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .Where(IsPodFile)
                .ToList();

            if (filePaths.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到POD文件");
                OnExtractionFailed("未找到POD文件");
                return;
            }

            TotalFilesToExtract = filePaths.Count;

            try
            {
                foreach (var filePath in filePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");

                    try
                    {
                        var podFile = new PodFile(filePath);
                        var extractedCount = await ExtractPodFile(podFile, extractedDir, cancellationToken);
                        extractedFiles.AddRange(extractedCount);

                        foreach (var file in extractedCount)
                        {
                            OnFileExtracted(file);
                        }

                        ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(filePath)} -> {extractedCount.Count}个文件");
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                    }
                }

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "解包操作已取消");
                OnExtractionFailed("解包操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包失败:{ex.Message}");
                OnExtractionFailed($"解包失败:{ex.Message}");
            }
        }

        private async Task<List<string>> ExtractPodFile(PodFile podFile, string outputDir, CancellationToken cancellationToken)
        {
            var extractedFiles = new List<string>();

            string baseFileName = Path.GetFileNameWithoutExtension(podFile.FilePath);
            string podOutputDir = Path.Combine(outputDir, baseFileName);

            if (Directory.Exists(podOutputDir))
            {
                Directory.Delete(podOutputDir, true);
            }
            Directory.CreateDirectory(podOutputDir);

            foreach (var fileEntry in podFile.FileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fullOutputPath = Path.Combine(podOutputDir, fileEntry.Key);
                string outputDirectory = Path.GetDirectoryName(fullOutputPath) ?? podOutputDir;

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                try
                {
                    var fileData = podFile.ReadFile(fileEntry.Key);
                    await File.WriteAllBytesAsync(fullOutputPath, fileData, cancellationToken);
                    if (fileData.Length >= 4)
                    {
                        if (fileData[0] == 0x52 && fileData[1] == 0x49 &&
                            fileData[2] == 0x46 && fileData[3] == 0x58)
                        {
                            string wemFilePath = Path.ChangeExtension(fullOutputPath, ".wem");
                            if (File.Exists(wemFilePath))
                            {
                                File.Delete(wemFilePath);
                            }
                            File.Move(fullOutputPath, wemFilePath);
                            fullOutputPath = wemFilePath;
                            ExtractionProgress?.Invoke(this, $"识别为WEM文件，已重命名:{fileEntry.Key} -> {Path.GetFileName(wemFilePath)}");
                        }
                    }

                    if (fileEntry.Value.Timestamp > 0)
                    {
                        var dateTime = DateTimeOffset.FromUnixTimeSeconds(fileEntry.Value.Timestamp).DateTime;
                        File.SetCreationTime(fullOutputPath, dateTime);
                        File.SetLastWriteTime(fullOutputPath, dateTime);
                    }

                    extractedFiles.Add(fullOutputPath);
                    ExtractionProgress?.Invoke(this, $"已提取:{fileEntry.Key}");
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"提取文件{fileEntry.Key}时出错:{ex.Message}");
                }
            }

            return extractedFiles;
        }

        private bool IsPodFile(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                if (fs.Length < 4) return false;

                byte[] header = new byte[4];
                fs.Read(header, 0, 4);

                string magic = Encoding.ASCII.GetString(header);
                return magic == "POD1" || magic == "POD2" || magic == "POD3" ||
                       magic == "POD4" || magic == "POD5" || magic == "POD6" ||
                       magic == "dtxe";
            }
            catch
            {
                return false;
            }
        }
    }

    public class PodFileMetadata
    {
        public string Name { get; set; } = string.Empty;
        public uint Size { get; set; }
        public uint Offset { get; set; }
        public uint Timestamp { get; set; }
        public uint Checksum { get; set; }
        public uint PathOffset { get; set; }
        public uint UncompressedSize { get; set; }
        public uint CompressionLevel { get; set; }
        public uint Flags { get; set; }
        public uint Zero { get; set; }
    }

    public class PodFile
    {
        public string FilePath { get; }
        public string Magic { get; private set; } = string.Empty;
        public uint FileCount { get; private set; }
        public uint IndexOffset { get; private set; }
        public string Comment { get; private set; } = string.Empty;
        public uint Version { get; private set; }
        public uint Checksum { get; private set; }
        public uint AuditFileCount { get; private set; }
        public uint Revision { get; private set; }
        public uint Priority { get; private set; }
        public string Author { get; private set; } = string.Empty;
        public string Copyright { get; private set; } = string.Empty;
        public uint SizeIndex { get; private set; }
        public string NextPodFile { get; private set; } = string.Empty;

        public Dictionary<string, PodFileMetadata> FileEntries { get; } = new Dictionary<string, PodFileMetadata>();

        public PodFile(string filePath)
        {
            FilePath = filePath;
            ParseHeader();
            ParseFileTable();
        }

        private uint ReadUInt32(BinaryReader reader)
        {
            byte[] data = reader.ReadBytes(4);
            if (data.Length != 4)
                throw new IOException("文件意外结束");
            return BitConverter.ToUInt32(data, 0);
        }

        private string GetCString(byte[] data)
        {
            return Encoding.ASCII.GetString(data).Split('\0')[0];
        }

        private void ParseHeader()
        {
            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            byte[] magicBytes = reader.ReadBytes(4);
            Magic = Encoding.ASCII.GetString(magicBytes);

            const int COMMENT_LENGTH_POD = 80;
            const int COMMENT_LENGTH_EPD = 256;
            const int AUTHOR_LENGTH = 80;
            const int COPYRIGHT_LENGTH = 80;
            const int NEXT_ARCHIVE_LENGTH = 80;

            if (Magic == "dtxe")
            {
                Magic = "EPD";
                Comment = GetCString(reader.ReadBytes(COMMENT_LENGTH_EPD));
                FileCount = ReadUInt32(reader);
                Version = ReadUInt32(reader);
                Checksum = ReadUInt32(reader);
                IndexOffset = (uint)reader.BaseStream.Position;
            }
            else if (Magic == "POD2")
            {
                Checksum = ReadUInt32(reader);
                Comment = GetCString(reader.ReadBytes(COMMENT_LENGTH_POD));
                FileCount = ReadUInt32(reader);
                AuditFileCount = ReadUInt32(reader);
                IndexOffset = (uint)reader.BaseStream.Position;
            }
            else if (Magic == "POD3" || Magic == "POD4" || Magic == "POD5")
            {
                Checksum = ReadUInt32(reader);
                Comment = GetCString(reader.ReadBytes(COMMENT_LENGTH_POD));
                FileCount = ReadUInt32(reader);
                AuditFileCount = ReadUInt32(reader);
                Revision = ReadUInt32(reader);
                Priority = ReadUInt32(reader);
                Author = GetCString(reader.ReadBytes(AUTHOR_LENGTH));
                Copyright = GetCString(reader.ReadBytes(COPYRIGHT_LENGTH));
                IndexOffset = ReadUInt32(reader);
                ReadUInt32(reader);
                SizeIndex = ReadUInt32(reader);
                ReadUInt32(reader);
                ReadUInt32(reader);
                ReadUInt32(reader);

                if (Magic == "POD5")
                {
                    NextPodFile = GetCString(reader.ReadBytes(NEXT_ARCHIVE_LENGTH));
                }
            }
            else if (Magic == "POD6")
            {
                FileCount = ReadUInt32(reader);
                Version = ReadUInt32(reader);
                IndexOffset = ReadUInt32(reader);
                SizeIndex = ReadUInt32(reader);
                reader.BaseStream.Seek(IndexOffset, SeekOrigin.Begin);
            }
            else
            {
                Magic = "POD1";
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                FileCount = ReadUInt32(reader);
                Comment = GetCString(reader.ReadBytes(COMMENT_LENGTH_POD));
                IndexOffset = (uint)reader.BaseStream.Position;
            }
        }

        private void ParseFileTable()
        {
            FileEntries.Clear();

            int dirEntrySize = Magic switch
            {
                "POD1" => 40,
                "EPD" => 80,
                "POD2" or "POD3" => 20,
                "POD6" => 24,
                _ => 28
            };

            const int FILE_NAME_LENGTH = 256;
            const int FILE_NAME_LENGTH_EPD = 64;
            const int FILE_NAME_LENGTH_POD1 = 32;

            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            for (uint index = 0; index < FileCount; index++)
            {
                var metadata = new PodFileMetadata();

                if (Magic == "POD1")
                {
                    string fileName = GetCString(reader.ReadBytes(FILE_NAME_LENGTH_POD1));
                    metadata.Size = ReadUInt32(reader);
                    metadata.Offset = ReadUInt32(reader);
                    metadata.UncompressedSize = metadata.Size;
                    FileEntries[fileName] = metadata;
                }
                else if (Magic == "EPD")
                {
                    string fileName = GetCString(reader.ReadBytes(FILE_NAME_LENGTH_EPD));
                    metadata.Size = ReadUInt32(reader);
                    metadata.Offset = ReadUInt32(reader);
                    metadata.Timestamp = ReadUInt32(reader);
                    metadata.Checksum = ReadUInt32(reader);
                    metadata.UncompressedSize = metadata.Size;
                    FileEntries[fileName] = metadata;
                }
                else if (Magic == "POD6")
                {
                    reader.BaseStream.Seek(IndexOffset + (index * dirEntrySize), SeekOrigin.Begin);
                    metadata.PathOffset = ReadUInt32(reader);
                    metadata.Size = ReadUInt32(reader);
                    metadata.Offset = ReadUInt32(reader);
                    metadata.UncompressedSize = ReadUInt32(reader);
                    metadata.Flags = ReadUInt32(reader);
                    metadata.Zero = ReadUInt32(reader);

                    reader.BaseStream.Seek(IndexOffset + (FileCount * dirEntrySize) + metadata.PathOffset, SeekOrigin.Begin);
                    string fileName = GetCString(reader.ReadBytes(FILE_NAME_LENGTH));

                    if (metadata.Size != metadata.UncompressedSize && (metadata.Flags & 8) == 0)
                    {
                        throw new WarningException($"发现文件{fileName}的压缩和未压缩大小不匹配");
                    }

                    FileEntries[fileName] = metadata;
                }
                else
                {
                    reader.BaseStream.Seek(IndexOffset + (index * dirEntrySize), SeekOrigin.Begin);
                    metadata.PathOffset = ReadUInt32(reader);
                    metadata.Size = ReadUInt32(reader);
                    metadata.Offset = ReadUInt32(reader);

                    if (Magic == "POD4" || Magic == "POD5")
                    {
                        metadata.UncompressedSize = ReadUInt32(reader);
                        metadata.CompressionLevel = ReadUInt32(reader);
                    }
                    else
                    {
                        metadata.UncompressedSize = metadata.Size;
                    }

                    metadata.Timestamp = ReadUInt32(reader);
                    metadata.Checksum = ReadUInt32(reader);

                    reader.BaseStream.Seek(IndexOffset + (FileCount * dirEntrySize) + metadata.PathOffset, SeekOrigin.Begin);
                    string fileName = GetCString(reader.ReadBytes(FILE_NAME_LENGTH));

                    if (metadata.Size != metadata.UncompressedSize && metadata.CompressionLevel == 0)
                    {
                        throw new WarningException($"发现文件{fileName}的压缩和未压缩大小不匹配");
                    }

                    fileName = fileName.Replace("\\", Path.DirectorySeparatorChar.ToString());
                    FileEntries[fileName] = metadata;
                }
            }
        }

        public byte[] ReadFile(string fileName)
        {
            if (!FileEntries.TryGetValue(fileName, out var metadata))
                throw new FileNotFoundException($"文件{fileName}不在POD归档中");

            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            reader.BaseStream.Seek(metadata.Offset, SeekOrigin.Begin);
            return reader.ReadBytes((int)metadata.Size);
        }
    }
}