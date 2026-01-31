using System.Text;

namespace super_toolbox
{
    public class Darc_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] DARC_MAGIC_BYTES = new byte[] { 0x64, 0x61, 0x72, 0x63 };
        private static readonly ushort DARC_BOM = 0xFEFF;
        private static readonly ushort DARC_HEADER_LEN = 0x1C;
        private static readonly uint DARC_VERSION = 0x01000000;
        private static readonly uint DARC_MAGIC = BitConverter.ToUInt32(DARC_MAGIC_BYTES, 0);

        private struct DarcHeader
        {
            public uint Magic;
            public ushort Bom;
            public ushort HeaderLen;
            public uint Version;
            public uint FileSize;
            public uint TableOffset;
            public uint TableSize;
            public uint FileDataOffset;
        }

        private struct DarcTableEntry
        {
            public uint FilenameOffset;
            public uint Offset;
            public uint Size;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            var darcFiles = new List<string>();

            foreach (var file in allFiles)
            {
                if (IsDarcFile(file))
                {
                    string darcFilePath = EnsureDarcExtension(file);
                    darcFiles.Add(darcFilePath);
                }
            }

            if (darcFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到DARC格式文件");
                OnExtractionFailed("未找到DARC格式文件");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理{darcFiles.Count}个DARC文件");

            try
            {
                int totalExtractedFiles = 0;

                foreach (var darcFile in darcFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string fileName = Path.GetFileName(darcFile);
                    ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                    string fileDirectory = Path.GetDirectoryName(darcFile) ?? directoryPath;
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(darcFile);
                    string outputDir = Path.Combine(fileDirectory, fileNameWithoutExtension);

                    if (Directory.Exists(outputDir))
                    {
                        Directory.Delete(outputDir, true);
                    }
                    Directory.CreateDirectory(outputDir);

                    var extractedFiles = await ExtractDarcFile(darcFile, outputDir, cancellationToken);
                    totalExtractedFiles += extractedFiles.Count;

                    foreach (var file in extractedFiles)
                    {
                        OnFileExtracted(file);
                    }

                    ExtractionProgress?.Invoke(this, $"完成处理:{fileName} -> {extractedFiles.Count}个文件");
                }

                TotalFilesToExtract = totalExtractedFiles;
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

        private string EnsureDarcExtension(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension))
            {
                string newFilePath = $"{filePath}.darc";
                if (!File.Exists(newFilePath))
                {
                    File.Move(filePath, newFilePath);
                    ExtractionProgress?.Invoke(this, $"为无后缀DARC文件添加后缀: {Path.GetFileName(filePath)} -> {Path.GetFileName(newFilePath)}");
                    return newFilePath;
                }
            }
            return filePath;
        }

        private async Task<List<string>> ExtractDarcFile(string inputFile, string outputDir, CancellationToken cancellationToken)
        {
            var extractedFiles = new List<string>();

            using (var fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            {
                var header = ReadDarcHeader(fs);
                ValidateHeader(header);

                if ((ulong)header.TableOffset >= (ulong)fs.Length ||
                    (ulong)header.TableSize >= (ulong)fs.Length ||
                    (ulong)header.TableOffset + (ulong)header.TableSize >= (ulong)fs.Length ||
                    (ulong)header.FileDataOffset >= (ulong)fs.Length)
                {
                    throw new Exception("文件头中的偏移量/大小字段对此文件大小无效");
                }

                fs.Seek(header.TableOffset, SeekOrigin.Begin);
                var tableData = new byte[header.TableSize];
                await fs.ReadAsync(tableData, 0, (int)header.TableSize, cancellationToken);

                DarcTableEntry firstEntry = ReadTableEntry(tableData, 0);
                uint totalEntries = firstEntry.Size & 0xFFFFFF;
                uint fileNameTableOffset = totalEntries * 0x0C;

                if (header.TableSize < fileNameTableOffset)
                {
                    throw new Exception("表格中第一个条目的大小字段太大");
                }

                if (totalEntries < 2)
                {
                    throw new Exception("表格条目总数无效");
                }

                ExtractionProgress?.Invoke(this, $"总表格条目数:0x{totalEntries:X}");

                string baseArcPath = Path.DirectorySeparatorChar.ToString();
                string fsDirectoryPath = outputDir;

                int ret = await ExtractRecursive(fs, tableData, fileNameTableOffset, totalEntries, fsDirectoryPath, baseArcPath, 2, totalEntries, cancellationToken);

                if (ret != 0)
                {
                    throw new Exception($"解包失败,错误码:{ret}");
                }

                extractedFiles.AddRange(Directory.GetFiles(outputDir, "*.*", SearchOption.AllDirectories)
                    .Select(file => GetRelativePath(outputDir, file)));
            }

            return extractedFiles;
        }

        private async Task<int> ExtractRecursive(FileStream fs, byte[] tableData, uint fileNameTableOffset, uint tableSize,
            string fsDirectoryPath, string baseArcPath, uint startEntry, uint endEntry, CancellationToken cancellationToken)
        {
            for (uint pos = startEntry; pos < endEntry; pos++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint entryOffset = pos * 0x0C;
                var entry = ReadTableEntry(tableData, entryOffset);

                uint fnOffsetRaw = entry.FilenameOffset;
                uint fnOffset = fnOffsetRaw & 0xFFFFFF;
                bool isDirectory = (fnOffsetRaw & 0x01000000) != 0;

                if (fnOffset >= tableSize - fileNameTableOffset)
                {
                    ExtractionProgress?.Invoke(this, "文件名偏移无效");
                    return 8;
                }

                string fileName = ReadUtf16String(tableData, fileNameTableOffset + fnOffset);
                string arcPath = baseArcPath + fileName;
                string fsPath = Path.Combine(fsDirectoryPath, arcPath.TrimStart(Path.DirectorySeparatorChar));

                ExtractionProgress?.Invoke(this, arcPath);

                if (isDirectory)
                {
                    if (entry.Size > endEntry)
                    {
                        ExtractionProgress?.Invoke(this, "目录大小值无效");
                        return 10;
                    }

                    if (!string.IsNullOrEmpty(fsPath))
                    {
                        Directory.CreateDirectory(fsPath);
                    }

                    int pos2 = baseArcPath.Length;
                    string tempPath = baseArcPath + fileName + Path.DirectorySeparatorChar;
                    baseArcPath = tempPath;

                    int ret = await ExtractRecursive(fs, tableData, fileNameTableOffset, tableSize, fsDirectoryPath, baseArcPath, pos + 1, entry.Size, cancellationToken);
                    if (ret != 0) return ret;

                    pos = entry.Size - 1;
                    baseArcPath = baseArcPath.Remove(pos2);
                }
                else
                {
                    long fileSize = fs.Length;
                    long fileOffset = entry.Offset;
                    long fileEnd = fileOffset + entry.Size;

                    if ((fileOffset >= fileSize) || ((long)entry.Size >= fileSize) || (fileEnd > fileSize))
                    {
                        ExtractionProgress?.Invoke(this, "此文件的偏移量/大小字段对此档案无效");
                        return 4;
                    }

                    fs.Seek(fileOffset, SeekOrigin.Begin);
                    var fileData = new byte[entry.Size];
                    int readBytes = await fs.ReadAsync(fileData, 0, (int)entry.Size, cancellationToken);
                    if (readBytes != entry.Size)
                    {
                        ExtractionProgress?.Invoke(this, "从档案读取文件数据失败");
                        return 12;
                    }

                    string? dirPath = Path.GetDirectoryName(fsPath);
                    if (!string.IsNullOrEmpty(dirPath))
                    {
                        Directory.CreateDirectory(dirPath);
                    }

                    await File.WriteAllBytesAsync(fsPath, fileData, cancellationToken);
                }
            }

            return 0;
        }

        private DarcHeader ReadDarcHeader(Stream stream)
        {
            var buffer = new byte[0x1C];
            stream.Read(buffer, 0, buffer.Length);
            stream.Seek(0, SeekOrigin.Begin);

            return new DarcHeader
            {
                Magic = GetUInt32(buffer, 0),
                Bom = GetUInt16(buffer, 4),
                HeaderLen = GetUInt16(buffer, 6),
                Version = GetUInt32(buffer, 8),
                FileSize = GetUInt32(buffer, 0x0C),
                TableOffset = GetUInt32(buffer, 0x10),
                TableSize = GetUInt32(buffer, 0x14),
                FileDataOffset = GetUInt32(buffer, 0x18)
            };
        }

        private void ValidateHeader(DarcHeader header)
        {
            if (header.Magic != DARC_MAGIC)
                throw new Exception("无效的档案魔数");
            if (header.Bom != DARC_BOM)
                throw new Exception("无效的档案BOM");
            if (header.HeaderLen != DARC_HEADER_LEN)
                throw new Exception("无效的档案头部长度");
            if (header.Version != DARC_VERSION)
                throw new Exception("无效的档案版本");
        }

        private DarcTableEntry ReadTableEntry(byte[] data, uint offset)
        {
            int idx = (int)offset;
            return new DarcTableEntry
            {
                FilenameOffset = GetUInt32(data, idx),
                Offset = GetUInt32(data, idx + 4),
                Size = GetUInt32(data, idx + 8)
            };
        }

        private string ReadUtf16String(byte[] data, uint offset)
        {
            var list = new List<byte>();
            int idx = (int)offset;

            while (idx + 1 < data.Length)
            {
                ushort c = GetUInt16(data, idx);
                if (c == 0) break;

                list.Add((byte)(c & 0xFF));
                list.Add((byte)(c >> 8));
                idx += 2;
            }

            return Encoding.Unicode.GetString(list.ToArray());
        }

        private uint GetUInt32(byte[] data, int offset)
        {
            if (offset + 3 >= data.Length) return 0;
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private ushort GetUInt16(byte[] data, int offset)
        {
            if (offset + 1 >= data.Length) return 0;
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;
            Uri baseUri = new Uri(basePath);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        private bool IsDarcFile(string filePath)
        {
            try
            {
                FileInfo fi = new FileInfo(filePath);
                if (fi.Attributes.HasFlag(FileAttributes.Directory) || fi.Length < DARC_HEADER_LEN)
                    return false;

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] header = new byte[DARC_MAGIC_BYTES.Length];
                    int read = fs.Read(header, 0, header.Length);
                    if (read != header.Length) return false;

                    for (int i = 0; i < header.Length; i++)
                    {
                        if (header[i] != DARC_MAGIC_BYTES[i])
                            return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
