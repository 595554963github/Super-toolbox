using System.Text;
using System.Security.Cryptography;

namespace super_toolbox
{
    public class Artemis_pfs_Extractor : BaseExtractor
    {
        private const int ARCHIVE_MAGIC_0 = 0x70;
        private const int ARCHIVE_MAGIC_1 = 0x66;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath} 不是有效的目录");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .ToList();

            var pfsFiles = new List<string>();

            foreach (var file in allFiles)
            {
                if (IsPfsFile(file))
                {
                    pfsFiles.Add(file);
                }
            }

            TotalFilesToExtract = pfsFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{TotalFilesToExtract}个PFS文件");

            int totalExtractedFiles = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var pfsFilePath in pfsFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string pfsFileName = Path.GetFileName(pfsFilePath);
                            string folderName = pfsFileName.Replace(".", "");
                            string pfsExtractDir = Path.Combine(Path.GetDirectoryName(pfsFilePath)!, folderName);

                            if (Directory.Exists(pfsExtractDir))
                            {
                                ExtractionProgress?.Invoke(this, $"目录已存在,跳过:{pfsFileName}");
                                continue;
                            }

                            Directory.CreateDirectory(pfsExtractDir);

                            ExtractionProgress?.Invoke(this, $"正在处理:{pfsFileName}");

                            int extractedCount = UnpackPfsFile(pfsFilePath, pfsExtractDir, cancellationToken);
                            totalExtractedFiles += extractedCount;

                            ExtractionProgress?.Invoke(this, $"完成处理:{pfsFileName}->{extractedCount}个文件");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(pfsFilePath)}时出错:{ex.Message}");
                            OnExtractionFailed($"处理{Path.GetFileName(pfsFilePath)}时出错:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"提取完成,总共提取了{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        private bool IsPfsFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] header = new byte[2];
                    if (fs.Read(header, 0, 2) == 2)
                    {
                        return header[0] == ARCHIVE_MAGIC_0 && header[1] == ARCHIVE_MAGIC_1;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        private int UnpackPfsFile(string pfsFilePath, string outputDir, CancellationToken cancellationToken)
        {
            int extractedCount = 0;

            using (var fileStream = new FileStream(pfsFilePath, FileMode.Open, FileAccess.Read))
            {
                var header = ReadHeader(fileStream);

                var entries = ReadIndex(fileStream, header);

                extractedCount = ProcessFiles(fileStream, entries, outputDir, header, cancellationToken);
            }

            return extractedCount;
        }

        private ArtemisHeader ReadHeader(FileStream stream)
        {
            byte[] buffer = new byte[7];
            stream.Read(buffer, 0, 7);

            byte[] magic = new byte[2] { buffer[0], buffer[1] };

            if (magic[0] != ARCHIVE_MAGIC_0 || magic[1] != ARCHIVE_MAGIC_1)
            {
                throw new InvalidDataException("无效的阿尔特弥斯PFS档案!");
            }

            byte packVersion = buffer[2];
            uint indexSize = BitConverter.ToUInt32(buffer, 3);

            if (packVersion == (byte)'2')
            {
                stream.Seek(4, SeekOrigin.Current);
            }

            byte[] countBuffer = new byte[4];
            stream.Read(countBuffer, 0, 4);
            uint fileCount = BitConverter.ToUInt32(countBuffer, 0);

            return new ArtemisHeader
            {
                Magic = magic,
                PackVersion = packVersion,
                IndexSize = indexSize,
                FileCount = fileCount
            };
        }

        private List<ArtemisEntry> ReadIndex(FileStream stream, ArtemisHeader header)
        {
            var entries = new List<ArtemisEntry>();

            for (int i = 0; i < header.FileCount; i++)
            {
                byte[] pathLenBuf = new byte[4];
                stream.Read(pathLenBuf, 0, 4);
                int pathLen = (int)BitConverter.ToUInt32(pathLenBuf, 0);

                byte[] pathBuf = new byte[pathLen];
                stream.Read(pathBuf, 0, pathLen);
                string path = Encoding.UTF8.GetString(pathBuf);

                int reserved = header.PackVersion switch
                {
                    (byte)'2' => 12,
                    (byte)'8' => 4,
                    _ => 4
                };

                stream.Seek(reserved, SeekOrigin.Current);

                byte[] offsetBuf = new byte[4];
                stream.Read(offsetBuf, 0, 4);
                uint offset = BitConverter.ToUInt32(offsetBuf, 0);

                byte[] sizeBuf = new byte[4];
                stream.Read(sizeBuf, 0, 4);
                uint size = BitConverter.ToUInt32(sizeBuf, 0);

                entries.Add(new ArtemisEntry
                {
                    Path = path,
                    Offset = offset,
                    Size = size
                });
            }

            return entries;
        }

        private int ProcessFiles(FileStream stream, List<ArtemisEntry> entries, string outputDir, ArtemisHeader header, CancellationToken cancellationToken)
        {
            byte[] xorKey = Array.Empty<byte>();

            if (header.PackVersion == (byte)'8')
            {
                using (var sha1 = SHA1.Create())
                {
                    byte[] indexData = new byte[header.IndexSize];
                    stream.Seek(7, SeekOrigin.Begin);
                    stream.Read(indexData, 0, (int)header.IndexSize);
                    xorKey = sha1.ComputeHash(indexData);
                }
            }

            int processed = 0;
            int validEntries = entries.Count(e => e.Offset != 0);

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.Offset == 0)
                    continue;

                string entryPath = entry.Path.Replace('\\', Path.DirectorySeparatorChar);
                string outputPath = Path.Combine(outputDir, entryPath);
                string? parentDir = Path.GetDirectoryName(outputPath);

                if (!string.IsNullOrEmpty(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                byte[] buffer = new byte[entry.Size];

                stream.Seek(entry.Offset, SeekOrigin.Begin);
                stream.Read(buffer, 0, (int)entry.Size);

                if (header.PackVersion == (byte)'8' && xorKey.Length > 0)
                {
                    XorCrypt(buffer, xorKey);
                }

                File.WriteAllBytes(outputPath, buffer);
                processed++;

                OnFileExtracted(entryPath);

                if (processed % 10 == 0 || processed == validEntries)
                {
                    ExtractionProgress?.Invoke(this, $"正在提取:{entry.Path} ({processed}/{validEntries})");
                }
            }

            return processed;
        }

        private void XorCrypt(byte[] data, byte[] key)
        {
            if (key.Length == 0)
                return;

            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= key[i % key.Length];
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private class ArtemisHeader
        {
            public byte[] Magic { get; set; } = Array.Empty<byte>();
            public byte PackVersion { get; set; }
            public uint IndexSize { get; set; }
            public uint FileCount { get; set; }
        }

        private class ArtemisEntry
        {
            public string Path { get; set; } = string.Empty;
            public uint Offset { get; set; }
            public uint Size { get; set; }
        }
    }
}