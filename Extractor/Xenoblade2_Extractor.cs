using System.Text;

namespace super_toolbox
{
    public class Xenoblade2_Extractor : BaseExtractor
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
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理Xenoblade2档案文件");

            try
            {
                await Task.Run(() => ProcessXb2Files(directoryPath, cancellationToken), cancellationToken);

                ExtractionProgress?.Invoke(this, $"提取完成:提取了{ExtractedFileCount}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理过程中出现错误:{ex.Message}");
                OnExtractionFailed($"处理过程中出现错误:{ex.Message}");
            }
        }

        private void ProcessXb2Files(string directoryPath, CancellationToken cancellationToken)
        {
            var arhFiles = Directory.GetFiles(directoryPath, "*.arh", SearchOption.AllDirectories);

            if (arhFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.arh文件");
                return;
            }

            int processedCount = 0;
            foreach (var arhFile in arhFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedCount++;

                string arhFileName = Path.GetFileNameWithoutExtension(arhFile);
                string baseName = Path.GetFileNameWithoutExtension(arhFile);
                string directory = Path.GetDirectoryName(arhFile) ?? "";
                string ardFile = Path.Combine(directory, baseName + ".ard");

                string archiveOutputDir = Path.Combine(directory, arhFileName);

                ExtractionProgress?.Invoke(this, $"正在处理档案文件:{arhFileName} ({processedCount}/{arhFiles.Length})");

                if (!File.Exists(ardFile))
                {
                    ExtractionError?.Invoke(this, $"错误:找不到对应的.ard文件:{Path.GetFileName(ardFile)}");
                    continue;
                }

                try
                {
                    ExtractArchive(arhFile, ardFile, archiveOutputDir, cancellationToken);
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{arhFileName}时出错:{ex.Message}");
                }
            }
        }

        private void ExtractArchive(string arhFile, string ardFile, string outputDir, CancellationToken cancellationToken)
        {
            try
            {
                using var archive = new Xb2FileArchive(arhFile, ardFile);
                var fileInfos = archive.FileInfo.Where(x => !string.IsNullOrWhiteSpace(x.Filename)).ToArray();

                ExtractionProgress?.Invoke(this, $"档案中包含{fileInfos.Length}个文件");

                TotalFilesToExtract = fileInfos.Length;

                foreach (var file in fileInfos)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fullPath = Path.Combine(outputDir, file.Filename!.TrimStart('/'));
                    string dir = Path.GetDirectoryName(fullPath) ?? outputDir;

                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    try
                    {
                        using (var outputStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                        {
                            archive.OutputFile(file, outputStream);
                        }

                        OnFileExtracted(fullPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(fullPath)}");
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"提取文件失败{file.Filename}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包档案文件时出错:{ex.Message}");
                throw;
            }
        }
    }

    public class Xb2FileArchive : IDisposable
    {
        private Node[] Nodes { get; }
        public Xb2FileInfo[] FileInfo { get; }
        public byte[] StringTable { get; }
        public int Field4 { get; }
        public int NodeCount { get; }
        public int StringTableOffset { get; }
        public int StringTableLength { get; }
        public int NodeTableOffset { get; }
        public int NodeTableLength { get; }
        public int FileTableOffset { get; }
        public int FileCount { get; }
        public uint Key { get; }
        private long Length { get; set; }

        private FileStream Stream { get; }

        public Xb2FileArchive(string headerFilename, string dataFilename)
        {
            if (!File.Exists(headerFilename))
                throw new FileNotFoundException($"ARH文件不存在:{headerFilename}");
            if (!File.Exists(dataFilename))
                throw new FileNotFoundException($"ARD文件不存在:{dataFilename}");

            byte[] headerFile = File.ReadAllBytes(headerFilename);
            DecryptArh(headerFile);

            using (var stream = new MemoryStream(headerFile))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 4;
                Field4 = reader.ReadInt32();
                NodeCount = reader.ReadInt32();
                StringTableOffset = reader.ReadInt32();
                StringTableLength = reader.ReadInt32();
                NodeTableOffset = reader.ReadInt32();
                NodeTableLength = reader.ReadInt32();
                FileTableOffset = reader.ReadInt32();
                FileCount = reader.ReadInt32();
                Key = reader.ReadUInt32() ^ 0xF3F35353;

                stream.Position = StringTableOffset;
                StringTable = reader.ReadBytes(StringTableLength);

                Nodes = new Node[NodeCount];
                stream.Position = NodeTableOffset;

                for (int i = 0; i < NodeCount; i++)
                {
                    Nodes[i] = new Node
                    {
                        Next = reader.ReadInt32(),
                        Prev = reader.ReadInt32()
                    };
                }

                FileInfo = new Xb2FileInfo[FileCount];
                stream.Position = FileTableOffset;

                for (int i = 0; i < FileCount; i++)
                {
                    FileInfo[i] = new Xb2FileInfo(reader);
                }

                AddAllFilenames();
            }

            Stream = new FileStream(dataFilename, FileMode.Open, FileAccess.Read);
            Length = Stream.Length;
        }

        public void OutputFile(Xb2FileInfo fileInfo, Stream outStream)
        {
            Stream.Position = fileInfo.Offset;

            if (fileInfo.Type == 2)
            {
                var buffer = new byte[fileInfo.CompressedSize];
                int bytesRead = Stream.Read(buffer, 0, fileInfo.CompressedSize);

                if (bytesRead != fileInfo.CompressedSize)
                {
                    throw new InvalidDataException($"读取数据失败:期望{fileInfo.CompressedSize}字节,实际{bytesRead}字节");
                }

                outStream.Write(buffer, 0, bytesRead);
            }
            else if (fileInfo.Type == 0)
            {
                var buffer = new byte[8192];
                long remaining = fileInfo.CompressedSize;

                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = Stream.Read(buffer, 0, toRead);
                    if (read == 0) break;

                    outStream.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
            else
            {
                Stream.Position = fileInfo.Offset;
                var buffer = new byte[fileInfo.CompressedSize];
                int read = Stream.Read(buffer, 0, fileInfo.CompressedSize);
                outStream.Write(buffer, 0, read);
            }
        }

        private void AddAllFilenames()
        {
            for (int i = 0; i < Nodes.Length; i++)
            {
                if (Nodes[i].Next >= 0 || Nodes[i].Prev < 0) continue;

                int offset = -Nodes[i].Next;
                if (offset < 0 || offset >= StringTable.Length) continue;

                while (offset < StringTable.Length && StringTable[offset] != 0)
                {
                    offset++;
                }
                offset++;

                if (offset + 3 >= StringTable.Length) continue;

                int fileId = BitConverter.ToInt32(StringTable, offset);
                if (fileId < 0 || fileId >= FileInfo.Length) continue;

                FileInfo[fileId].Filename = GetStringFromEndNode(i);
            }
        }

        private string GetStringFromEndNode(int endNodeIdx)
        {
            int cur = endNodeIdx;
            Node curNode = Nodes[cur];

            string nameSuffix = GetUTF8Z(StringTable, -curNode.Next) ?? "";
            var chars = new List<char>(nameSuffix.Reverse());

            while (curNode.Next != 0)
            {
                int prev = curNode.Prev;
                if (prev < 0 || prev >= Nodes.Length) break;

                Node prevNode = Nodes[prev];
                chars.Add((char)(cur ^ prevNode.Next));
                cur = prev;
                curNode = prevNode;
            }

            chars.Reverse();
            return new string(chars.ToArray());
        }

        public static void DecryptArh(byte[] file)
        {
            int fileLength = file.Length;
            if (fileLength < 40) return;

            var filei = new int[fileLength / 4];
            Buffer.BlockCopy(file, 0, filei, 0, (fileLength / 4) * 4);

            int key = (int)(filei[9] ^ 0xF3F35353);
            filei[9] = unchecked((int)0xF3F35353);

            int stringTableStart = filei[3] / 4;
            int nodeTableStart = filei[5] / 4;
            int stringTableEnd = stringTableStart + filei[4] / 4;
            int nodeTableEnd = nodeTableStart + filei[6] / 4;

            stringTableStart = Math.Max(0, Math.Min(stringTableStart, filei.Length - 1));
            stringTableEnd = Math.Max(0, Math.Min(stringTableEnd, filei.Length));
            nodeTableStart = Math.Max(0, Math.Min(nodeTableStart, filei.Length - 1));
            nodeTableEnd = Math.Max(0, Math.Min(nodeTableEnd, filei.Length));

            for (int i = stringTableStart; i < stringTableEnd; i++)
            {
                if (i < filei.Length)
                    filei[i] ^= key;
            }

            for (int i = nodeTableStart; i < nodeTableEnd; i++)
            {
                if (i < filei.Length)
                    filei[i] ^= key;
            }

            Buffer.BlockCopy(filei, 0, file, 0, (fileLength / 4) * 4);
        }

        private string? GetUTF8Z(byte[] data, int offset)
        {
            if (offset < 0 || offset >= data.Length) return null;

            int end = offset;
            while (end < data.Length && data[end] != 0)
            {
                end++;
            }

            if (end == offset) return "";

            return Encoding.UTF8.GetString(data, offset, end - offset);
        }

        private class Node
        {
            public int Next { get; set; }
            public int Prev { get; set; }
        }

        public void Dispose()
        {
            Stream?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public class Xb2FileInfo
    {
        public Xb2FileInfo(BinaryReader reader)
        {
            HeaderOffset = (int)reader.BaseStream.Position;
            Offset = reader.ReadInt64();
            CompressedSize = reader.ReadInt32();
            UncompressedSize = reader.ReadInt32();
            Type = reader.ReadInt32();
            Id = reader.ReadInt32();
            Filename = string.Empty;
        }

        public int HeaderOffset { get; }
        public string Filename { get; set; }
        public long Offset { get; }
        public int CompressedSize { get; }
        public int UncompressedSize { get; }
        public int Type { get; }
        public int Id { get; }
    }
}