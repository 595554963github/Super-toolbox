using System.Text;
using ZstdNet;

namespace super_toolbox
{
    public class Xenoblade3_Extractor : BaseExtractor
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
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            ExtractionStarted?.Invoke(this, $"开始处理Xenoblade3档案文件");

            try
            {
                await Task.Run(() => ProcessXb3Files(directoryPath, extractedDir, cancellationToken), cancellationToken);

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

        private void ProcessXb3Files(string directoryPath, string extractedDir, CancellationToken cancellationToken)
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

                string archiveOutputDir = Path.Combine(extractedDir, arhFileName);

                ExtractionProgress?.Invoke(this, $"正在处理归档文件:{arhFileName} ({processedCount}/{arhFiles.Length})");

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
                using var archive = new Xb3FileArchive(arhFile, ardFile);
                var fileInfos = archive.FileInfo;

                ExtractionProgress?.Invoke(this, $"档案中包含{fileInfos.Length}个文件");

                foreach (var fileInfo in fileInfos)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(fileInfo.Filename))
                        continue;

                    string filename = Path.Combine(outputDir, fileInfo.Filename.TrimStart('/'));
                    string dir = Path.GetDirectoryName(filename) ?? throw new InvalidOperationException();
                    Directory.CreateDirectory(dir);

                    try
                    {
                        byte[]? fileData = archive.ReadFile(fileInfo);
                        if (fileData != null)
                        {
                            File.WriteAllBytes(filename, fileData);
                            OnFileExtracted(filename);
                            ExtractionProgress?.Invoke(this, $"已提取:{fileInfo.Filename}");
                        }
                        else
                        {
                            ExtractionError?.Invoke(this, $"提取文件{fileInfo.Filename}失败:文件数据为空");
                        }
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"提取文件{fileInfo.Filename}时出错:{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包归档文件时出错:{ex.Message}");
                throw;
            }
        }
    }

    public class Xb3FileArchive : IDisposable
    {
        public ArchiveFileInfo[] FileInfo { get; }
        private byte[] StringTable { get; }
        private Node[] Nodes { get; }
        private uint Key { get; }
        private FileStream DataStream { get; }
        private string HeaderFilename { get; }
        private Decompressor? zstdDecompressor;

        public Xb3FileArchive(string headerFilename, string dataFilename)
        {
            HeaderFilename = headerFilename;

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
                int field4 = reader.ReadInt32();
                int nodeCount = reader.ReadInt32();
                int stringTableOffset = reader.ReadInt32();
                int stringTableLength = reader.ReadInt32();
                int nodeTableOffset = reader.ReadInt32();
                int nodeTableLength = reader.ReadInt32();
                int fileTableOffset = reader.ReadInt32();
                int fileCount = reader.ReadInt32();
                Key = reader.ReadUInt32() ^ 0xF3F35353;

                stream.Position = stringTableOffset;
                StringTable = reader.ReadBytes(stringTableLength);

                Nodes = new Node[nodeCount];
                stream.Position = nodeTableOffset;

                for (int i = 0; i < nodeCount; i++)
                {
                    Nodes[i] = new Node
                    {
                        Next = reader.ReadInt32(),
                        Prev = reader.ReadInt32()
                    };
                }

                FileInfo = new ArchiveFileInfo[fileCount];
                stream.Position = fileTableOffset;

                for (int i = 0; i < fileCount; i++)
                {
                    FileInfo[i] = new ArchiveFileInfo(reader);
                }

                AddAllFilenames();
            }

            DataStream = new FileStream(dataFilename, FileMode.Open, FileAccess.Read);

            zstdDecompressor = null;
        }

        public byte[]? ReadFile(string filename)
        {
            var fileInfo = GetFileInfo(filename);
            if (fileInfo == null)
                return null;

            return ReadFile(fileInfo);
        }

        public byte[]? ReadFile(ArchiveFileInfo fileInfo)
        {
            if (fileInfo.Offset + fileInfo.CompressedSize > DataStream.Length)
            {
                throw new InvalidDataException($"文件偏移超出数据流范围:偏移{fileInfo.Offset},大小{fileInfo.CompressedSize},总大小{DataStream.Length}");
            }

            DataStream.Position = fileInfo.Offset;

            switch (fileInfo.Type)
            {
                case 0: 
                    var output = new byte[fileInfo.CompressedSize];
                    DataStream.ReadExactly(output, 0, fileInfo.CompressedSize);
                    return output;

                case 2:
                    DataStream.Position = fileInfo.Offset + 0x30;
                    byte[] compressedData = new byte[fileInfo.CompressedSize];
                    DataStream.ReadExactly(compressedData, 0, fileInfo.CompressedSize);

                    return DecompressZStd(compressedData, fileInfo.UncompressedSize);

                default:
                    throw new NotSupportedException($"不支持的文件类型:{fileInfo.Type}");
            }
        }

        private ArchiveFileInfo? GetFileInfo(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            int cur = 0;
            Node curNode = Nodes[cur];

            for (int i = 0; i < filename.Length; i++)
            {
                if (curNode.Next < 0) break;

                int next = curNode.Next ^ char.ToLower(filename[i]);
                if (next < 0 || next >= Nodes.Length) return null;

                Node nextNode = Nodes[next];
                if (nextNode.Prev != cur) return null;
                cur = next;
                curNode = nextNode;
            }

            int offset = -curNode.Next;
            if (offset < 0 || offset >= StringTable.Length) return null;

            while (offset < StringTable.Length && StringTable[offset] != 0)
            {
                offset++;
            }
            offset++;

            if (offset + 3 >= StringTable.Length) return null;

            int fileId = BitConverter.ToInt32(StringTable, offset);
            if (fileId < 0 || fileId >= FileInfo.Length) return null;

            return FileInfo[fileId];
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
                if (fileId >= 0 && fileId < FileInfo.Length)
                {
                    string? filename = GetStringFromEndNode(i);
                    if (!string.IsNullOrEmpty(filename))
                    {
                        FileInfo[fileId].Filename = filename;
                    }
                    else
                    {
                        FileInfo[fileId].Filename = $"unknown_{fileId}";
                    }
                }
            }
        }

        private string? GetStringFromEndNode(int endNodeIdx)
        {
            if (endNodeIdx < 0 || endNodeIdx >= Nodes.Length)
                return null;

            int cur = endNodeIdx;
            Node curNode = Nodes[cur];

            int stringOffset = -curNode.Next;
            if (stringOffset < 0 || stringOffset >= StringTable.Length)
                return null;

            string? nameSuffix = GetUTF8Z(StringTable, stringOffset);
            if (nameSuffix == null)
                return null;

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

        private static string? GetUTF8Z(byte[] data, int offset)
        {
            if (offset < 0 || offset >= data.Length)
                return null;

            int end = offset;
            while (end < data.Length && data[end] != 0) end++;

            if (end == offset)
                return string.Empty;

            return Encoding.UTF8.GetString(data, offset, end - offset);
        }

        public static void DecryptArh(byte[] file)
        {
            if (file.Length < 40)
                return;

            var filei = new int[file.Length / 4];
            Buffer.BlockCopy(file, 0, filei, 0, file.Length);

            int key = (int)(filei[9] ^ 0xF3F35353);
            filei[9] = unchecked((int)0xF3F35353);

            int stringTableStart = filei[3] / 4;
            int nodeTableStart = filei[5] / 4;
            int stringTableEnd = stringTableStart + filei[4] / 4;
            int nodeTableEnd = nodeTableStart + filei[6] / 4;

            if (stringTableStart >= 0 && stringTableEnd <= filei.Length)
            {
                for (int i = stringTableStart; i < stringTableEnd; i++)
                {
                    filei[i] ^= key;
                }
            }

            if (nodeTableStart >= 0 && nodeTableEnd <= filei.Length)
            {
                for (int i = nodeTableStart; i < nodeTableEnd; i++)
                {
                    filei[i] ^= key;
                }
            }

            Buffer.BlockCopy(filei, 0, file, 0, file.Length);
        }

        private byte[] DecompressZStd(byte[] compressedData, int uncompressedSize)
        {
            try
            {
                if (zstdDecompressor == null)
                {
                    zstdDecompressor = new Decompressor();
                }

                byte[] decompressedData = zstdDecompressor.Unwrap(compressedData);

                if (decompressedData.Length != uncompressedSize)
                {
                    throw new InvalidDataException($"解压后大小不匹配:期望{uncompressedSize},实际{decompressedData.Length}");
                }

                return decompressedData;
            }
            catch (ZstdException zstdEx)
            {
                throw new InvalidDataException($"ZStd解压失败:{zstdEx.Message}", zstdEx);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"解压失败:{ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            zstdDecompressor?.Dispose();
            DataStream?.Dispose();
            GC.SuppressFinalize(this);
        }

        private class Node
        {
            public int Next { get; set; }
            public int Prev { get; set; }
        }
    }

    public class ArchiveFileInfo
    {
        public ArchiveFileInfo(BinaryReader reader)
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