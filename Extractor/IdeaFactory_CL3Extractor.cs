using System.Text;

namespace super_toolbox
{
    public class IdeaFactory_CL3Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private class CL3FileEntry
        {
            public string Name { get; set; } = string.Empty;
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public int LinkStartIndex { get; set; }
            public int LinkCount { get; set; }
            public int Id { get; set; }
        }

        private class CL3FileLink
        {
            public uint LinkedFileId { get; set; }
            public uint LinkId { get; set; }
        }

        private class CL3Section
        {
            public string Name { get; set; } = string.Empty;
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public int Count { get; set; }
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

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var cl3Files = Directory.GetFiles(directoryPath, "*.cl3", SearchOption.AllDirectories);
            if (cl3Files.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.cl3文件");
                OnExtractionFailed("未找到任何.cl3文件");
                return;
            }

            TotalFilesToExtract = cl3Files.Length;
            int processedFiles = 0;

            try
            {
                foreach (var cl3FilePath in cl3Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(cl3FilePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        string? parentDir = Path.GetDirectoryName(cl3FilePath);
                        if (string.IsNullOrEmpty(parentDir))
                        {
                            ExtractionError?.Invoke(this, $"无效路径:{cl3FilePath}");
                            OnExtractionFailed($"无效路径:{cl3FilePath}");
                            continue;
                        }

                        string cl3FileNameWithoutExt = Path.GetFileNameWithoutExtension(cl3FilePath);
                        string extractedFolder = Path.Combine(parentDir, cl3FileNameWithoutExt);

                        if (Directory.Exists(extractedFolder))
                            Directory.Delete(extractedFolder, true);
                        Directory.CreateDirectory(extractedFolder);

                        await Task.Run(() =>
                        {
                            ParseAndExtractCl3File(cl3FilePath, extractedFolder);
                        }, cancellationToken);

                        var allExtractedFiles = Directory.GetFiles(extractedFolder, "*", SearchOption.AllDirectories);
                        foreach (var extractedFile in allExtractedFiles)
                        {
                            OnFileExtracted(extractedFile);
                        }

                        ExtractionProgress?.Invoke(this, $"{Path.GetFileName(cl3FilePath)}解包完成,共提取{allExtractedFiles.Length}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{cl3FilePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{cl3FilePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,总提取文件数:{ExtractedFileCount}");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private void ParseAndExtractCl3File(string cl3FilePath, string outputFolder)
        {
            using (FileStream fs = new FileStream(cl3FilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                byte[] magic = reader.ReadBytes(3);
                if (magic[0] != 0x43 || magic[1] != 0x4C || magic[2] != 0x33)
                    throw new InvalidDataException("这不是CL3文件");

                bool isLittleEndian = reader.ReadChar() == 'L';

                fs.Seek(0x08, SeekOrigin.Current);

                uint sectionsCount = ReadUInt32(reader, isLittleEndian);
                uint sectionsOffset = ReadUInt32(reader, isLittleEndian);
                uint contentType = ReadUInt32(reader, isLittleEndian);

                List<CL3Section> sections = new List<CL3Section>();
                List<CL3FileEntry> fileEntries = new List<CL3FileEntry>();
                List<CL3FileLink> fileLinks = new List<CL3FileLink>();

                for (uint i = 0; i < sectionsCount; i++)
                {
                    fs.Seek(sectionsOffset + i * 0x50, SeekOrigin.Begin);

                    byte[] sectionNameBytes = reader.ReadBytes(0x20);
                    string sectionName = Encoding.UTF8.GetString(sectionNameBytes).TrimEnd('\0');

                    int sectionCount = ReadInt32(reader, isLittleEndian);
                    int sectionDataSize = ReadInt32(reader, isLittleEndian);
                    int sectionDataOffset = ReadInt32(reader, isLittleEndian);

                    CL3Section section = new CL3Section
                    {
                        Name = sectionName,
                        Count = sectionCount
                    };

                    if (sectionName == "FILE_COLLECTION")
                    {
                        for (int j = 0; j < sectionCount; j++)
                        {
                            long entryOffset = sectionDataOffset + j * 0x230;
                            fs.Seek(entryOffset, SeekOrigin.Begin);

                            byte[] fileNameBytes = reader.ReadBytes(0x200);
                            string fileName = Encoding.UTF8.GetString(fileNameBytes).TrimEnd('\0');

                            int fileId = ReadInt32(reader, isLittleEndian);
                            int fileOffset = ReadInt32(reader, isLittleEndian);
                            int fileSize = ReadInt32(reader, isLittleEndian);
                            int linkStartIndex = ReadInt32(reader, isLittleEndian);
                            int linkCount = ReadInt32(reader, isLittleEndian);

                            long dataPos = fileOffset + sectionDataOffset;
                            fs.Seek(dataPos, SeekOrigin.Begin);
                            byte[] fileData = reader.ReadBytes(fileSize);

                            CL3FileEntry entry = new CL3FileEntry
                            {
                                Name = fileName,
                                Id = fileId,
                                Data = fileData,
                                LinkStartIndex = linkStartIndex,
                                LinkCount = linkCount
                            };
                            fileEntries.Add(entry);
                        }
                    }
                    else if (sectionName == "FILE_LINK")
                    {
                        for (int j = 0; j < sectionCount; j++)
                        {
                            long linkOffset = sectionDataOffset + j * 0x20 + 0x04;
                            fs.Seek(linkOffset, SeekOrigin.Begin);

                            uint linkedFileId = ReadUInt32(reader, isLittleEndian);
                            uint linkId = ReadUInt32(reader, isLittleEndian);

                            CL3FileLink link = new CL3FileLink
                            {
                                LinkedFileId = linkedFileId,
                                LinkId = linkId
                            };
                            fileLinks.Add(link);
                        }
                    }
                    else
                    {
                        fs.Seek(sectionDataOffset, SeekOrigin.Begin);
                        section.Data = reader.ReadBytes(sectionDataSize);
                    }

                    sections.Add(section);
                }

                ExtractFilesToFolder(fileEntries, outputFolder);
            }
        }

        private void ExtractFilesToFolder(List<CL3FileEntry> fileEntries, string outputFolder)
        {
            foreach (var entry in fileEntries)
            {
                if (entry.Data.Length == 0)
                    continue;

                string safeFileName = GetSafeFileName(entry.Name);
                string outputPath = Path.Combine(outputFolder, safeFileName);

                string? directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(outputPath, entry.Data);
            }
        }

        private int ReadInt32(BinaryReader reader, bool isLittleEndian)
        {
            if (isLittleEndian)
                return reader.ReadInt32();

            byte[] bytes = reader.ReadBytes(4);
            Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        private uint ReadUInt32(BinaryReader reader, bool isLittleEndian)
        {
            if (isLittleEndian)
                return reader.ReadUInt32();

            byte[] bytes = reader.ReadBytes(4);
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private string GetSafeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string safeName = fileName;

            foreach (char c in invalidChars)
            {
                safeName = safeName.Replace(c, '_');
            }

            if (string.IsNullOrEmpty(safeName))
                safeName = "unnamed_file";

            return safeName;
        }
    }
}
