namespace super_toolbox
{
    public class AfsExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const long LARGE_FILE_THRESHOLD = 2L * 1024 * 1024 * 1024;
        private const uint AFS_HEADER_MAGIC = 0x00534641;
        private const uint AFS_HEADER_SIZE = 0x8;
        private const uint ENTRY_INFO_SIZE = 0x8;

        private static readonly byte[] AHX_START_HEADER = { 0x80, 0x00, 0x00, 0x20 };
        private static readonly byte[] AHX_END_HEADER = { 0x80, 0x01, 0x00, 0x0C, 0x41, 0x48, 0x58, 0x45, 0x28, 0x63, 0x29, 0x43, 0x52, 0x49, 0x00, 0x00 };
        private static readonly byte[] ADX_SIG_BYTES = { 0x80, 0x00 };
        private static readonly byte[] CRI_COPYRIGHT_BYTES = { 0x28, 0x63, 0x29, 0x43, 0x52, 0x49 };
        private static readonly byte[][] ADX_FIXED_SEQUENCES =
        {
            new byte[] { 0x03, 0x12, 0x04, 0x01, 0x00, 0x00 },
            new byte[] { 0x03, 0x12, 0x04, 0x02, 0x00, 0x00 }
        };

        private int _fileCounter = 0;

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

            var allFiles = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
            var afsFiles = new List<string>();

            ExtractionProgress?.Invoke(this, $"正在扫描文件头,共{allFiles.Count}个文件...");

            foreach (var file in allFiles)
            {
                try
                {
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (fs.Length < 8) continue;

                        byte[] header = new byte[4];
                        await fs.ReadAsync(header, 0, 4, cancellationToken);

                        uint magic = BitConverter.ToUInt32(header, 0);

                        if (magic == AFS_HEADER_MAGIC)
                        {
                            afsFiles.Add(file);
                            ExtractionProgress?.Invoke(this, $"发现AFS文件:{Path.GetFileName(file)}");
                        }
                    }
                }
                catch { }
            }

            if (afsFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何AFS格式的文件");
                OnExtractionFailed("未找到任何AFS格式的文件");
                return;
            }

            TotalFilesToExtract = afsFiles.Count;
            int processedFiles = 0;

            try
            {
                foreach (var afsFile in afsFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理AFS文件:{Path.GetFileName(afsFile)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        await ProcessAfsFileAsync(afsFile, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{afsFile}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,共提取{_fileCounter}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ProcessAfsFileAsync(string afsFilePath, CancellationToken cancellationToken)
        {
            string sourceFileName = Path.GetFileNameWithoutExtension(afsFilePath);
            if (string.IsNullOrEmpty(sourceFileName))
            {
                sourceFileName = "AFS_" + Guid.NewGuid().ToString().Substring(0, 8);
            }

            string outputDir = Path.Combine(Path.GetDirectoryName(afsFilePath) ?? "", sourceFileName);
            Directory.CreateDirectory(outputDir);

            FileInfo fileInfo = new FileInfo(afsFilePath);

            if (fileInfo.Length < LARGE_FILE_THRESHOLD)
            {
                await ProcessSmallAfsFileAsync(afsFilePath, outputDir, sourceFileName, cancellationToken);
            }
            else
            {
                await ProcessLargeAfsFileAsync(afsFilePath, outputDir, sourceFileName, cancellationToken);
            }
        }

        private async Task ProcessSmallAfsFileAsync(string afsFilePath, string outputDir, string sourceFileName, CancellationToken cancellationToken)
        {
            byte[] content = await File.ReadAllBytesAsync(afsFilePath, cancellationToken);

            if (content.Length < AFS_HEADER_SIZE + 8)
            {
                ExtractionError?.Invoke(this, $"AFS文件格式错误:{afsFilePath}");
                return;
            }

            uint entryCount = BitConverter.ToUInt32(content, 4);

            ExtractionProgress?.Invoke(this, $"AFS文件包含{entryCount}个条目");

            var entries = new List<AfsEntry>();

            for (int i = 0; i < entryCount; i++)
            {
                int entryOffsetPos = (int)(AFS_HEADER_SIZE + (i * ENTRY_INFO_SIZE));

                if (entryOffsetPos + 8 > content.Length)
                    break;

                uint offset = BitConverter.ToUInt32(content, entryOffsetPos);
                uint size = BitConverter.ToUInt32(content, entryOffsetPos + 4);

                if (offset == 0 && size == 0)
                    continue;

                uint minValidOffset = AFS_HEADER_SIZE + entryCount * ENTRY_INFO_SIZE;
                if (offset < minValidOffset)
                {
                    continue;
                }

                if (offset + size > content.Length)
                {
                    continue;
                }

                entries.Add(new AfsEntry
                {
                    Offset = offset,
                    Size = size
                });
            }

            entries = entries.OrderBy(e => e.Offset).ToList();

            int entrySequence = 1;
            foreach (var entry in entries)
            {
                ThrowIfCancellationRequested(cancellationToken);

                byte[] entryData = new byte[entry.Size];
                Array.Copy(content, entry.Offset, entryData, 0, entry.Size);

                string extension = DetectFileExtension(entryData);
                string outputFileName = $"{sourceFileName}_{entrySequence}{extension}";
                string outputFilePath = Path.Combine(outputDir, outputFileName);

                outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);
                await File.WriteAllBytesAsync(outputFilePath, entryData, cancellationToken);

                _fileCounter++;
                entrySequence++;
                OnFileExtracted(outputFilePath);
                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)}");
            }
        }

        private async Task ProcessLargeAfsFileAsync(string afsFilePath, string outputDir, string sourceFileName, CancellationToken cancellationToken)
        {
            using (var fs = new FileStream(afsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
            using (var reader = new BinaryReader(fs))
            {
                if (fs.Length < AFS_HEADER_SIZE + 8)
                {
                    ExtractionError?.Invoke(this, $"AFS文件格式错误:{afsFilePath}");
                    return;
                }

                fs.Seek(4, SeekOrigin.Begin);
                uint entryCount = reader.ReadUInt32();

                ExtractionProgress?.Invoke(this, $"AFS文件包含{entryCount}个条目");

                var entries = new List<AfsEntry>();

                for (int i = 0; i < entryCount; i++)
                {
                    uint offset = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();

                    if (offset == 0 && size == 0)
                        continue;

                    uint minValidOffset = AFS_HEADER_SIZE + entryCount * ENTRY_INFO_SIZE;
                    if (offset < minValidOffset)
                    {
                        continue;
                    }

                    if (offset + size > fs.Length)
                    {
                        continue;
                    }

                    entries.Add(new AfsEntry
                    {
                        Offset = offset,
                        Size = size
                    });
                }

                entries = entries.OrderBy(e => e.Offset).ToList();

                int entrySequence = 1;
                for (int i = 0; i < entries.Count; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    var entry = entries[i];

                    fs.Seek(entry.Offset, SeekOrigin.Begin);

                    byte[] entryData = new byte[entry.Size];
                    await fs.ReadAsync(entryData, 0, (int)entry.Size, cancellationToken);

                    string extension = DetectFileExtension(entryData);
                    string outputFileName = $"{sourceFileName}_{entrySequence}{extension}";
                    string outputFilePath = Path.Combine(outputDir, outputFileName);

                    outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                    await File.WriteAllBytesAsync(outputFilePath, entryData, cancellationToken);

                    _fileCounter++;
                    entrySequence++;
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)}");
                }
            }
        }

        private string DetectFileExtension(byte[] data)
        {
            if (data.Length < 4)
                return "";

            if (IsAhxFile(data))
                return ".ahx";

            if (IsAdxFile(data))
                return ".adx";

            uint headerMagic = BitConverter.ToUInt32(data, 0);
            if (headerMagic == 0x00414348 || headerMagic == 0x00C1C3C8)
                return ".hca";

            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46)
            {
                if (data.Length >= 12)
                {
                    if (data[8] == 0x57 && data[9] == 0x41 && data[10] == 0x56 && data[11] == 0x45)
                        return ".wav";
                    if (data[8] == 0x41 && data[9] == 0x54 && data[10] == 0x39 && (data[11] == 0x00 || data[11] == 0x20))
                        return ".at9";
                    if (data[8] == 0x58 && data[9] == 0x4D && data[10] == 0x41 && data[11] == 0x00)
                        return ".xma";
                    if (data.Length >= 20)
                    {
                        byte[] riffCheck = new byte[8];
                        Array.Copy(data, 16, riffCheck, 0, 8);
                        if ((riffCheck[0] == 0x42 && riffCheck[1] == 0x00 && riffCheck[2] == 0x00 && riffCheck[3] == 0x00 &&
                             riffCheck[4] == 0xFF && riffCheck[5] == 0xFF && riffCheck[6] == 0x02 && riffCheck[7] == 0x00) ||
                            (riffCheck[0] == 0x18 && riffCheck[1] == 0x00 && riffCheck[2] == 0x00 && riffCheck[3] == 0x00 &&
                             riffCheck[4] == 0x02 && riffCheck[5] == 0x00))
                        {
                            return ".wem";
                        }
                    }
                }
                return "";
            }

            if (data[0] == 0x58 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x52)
            {
                if (data.Length >= 20)
                {
                    byte[] rifxCheck = new byte[8];
                    Array.Copy(data, 16, rifxCheck, 0, 8);
                    if ((rifxCheck[0] == 0x00 && rifxCheck[1] == 0x00 && rifxCheck[2] == 0x00 && rifxCheck[3] == 0x20 &&
                         rifxCheck[4] == 0x01 && rifxCheck[5] == 0x65 && rifxCheck[6] == 0x00 && rifxCheck[7] == 0x10) ||
                        (rifxCheck[0] == 0x00 && rifxCheck[1] == 0x00 && rifxCheck[2] == 0x00 && rifxCheck[3] == 0x40 &&
                         rifxCheck[4] == 0x01 && rifxCheck[5] == 0x66 && rifxCheck[6] == 0x00 && rifxCheck[7] == 0x01))
                    {
                        return ".wem";
                    }
                }
                return "";
            }

            if (data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53)
                return ".ogg";

            if (data[0] == 0x46 && data[1] == 0x4F && data[2] == 0x52 && data[3] == 0x4D)
                return ".aix";

            if (data[0] == 0x41 && data[1] == 0x46 && data[2] == 0x53 && data[3] == 0x32)
                return ".awb";

            if (headerMagic == 0x70474156)
                return ".vag";

            if (headerMagic == 0x44495243)
                return ".usm";

            if (headerMagic == 0x694B4942)
                return ".bik";

            if (headerMagic == 0x6932424B || headerMagic == 0x6A32424B || headerMagic == 0x6E32424B)
                return ".bk2";

            if (data.Length >= 8 && BitConverter.ToUInt64(data, 0) == 0x11CF8E6675B22630)
                return ".wmv";

            return "";
        }

        private bool IsAhxFile(byte[] data)
        {
            if (data.Length < AHX_START_HEADER.Length)
                return false;

            for (int i = 0; i < AHX_START_HEADER.Length; i++)
            {
                if (data[i] != AHX_START_HEADER[i])
                    return false;
            }

            int endIndex = IndexOf(data, AHX_END_HEADER, AHX_START_HEADER.Length);
            if (endIndex == -1)
                return false;

            return true;
        }

        private bool IsAdxFile(byte[] data)
        {
            if (data.Length < 2 || data[0] != ADX_SIG_BYTES[0] || data[1] != ADX_SIG_BYTES[1])
                return false;

            int checkLength = Math.Min(10, data.Length);
            byte[] checkSegment = new byte[checkLength];
            Array.Copy(data, 0, checkSegment, 0, checkLength);

            bool hasFixedSequence = false;
            foreach (var seq in ADX_FIXED_SEQUENCES)
            {
                if (ContainsBytes(checkSegment, seq))
                {
                    hasFixedSequence = true;
                    break;
                }
            }

            if (!hasFixedSequence)
                return false;

            if (!ContainsBytes(data, CRI_COPYRIGHT_BYTES))
                return false;

            return true;
        }

        private static int IndexOf(byte[] source, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= source.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool ContainsBytes(byte[] data, byte[] pattern)
        {
            return IndexOf(data, pattern, 0) != -1;
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
            } while (File.Exists(newPath));

            return newPath;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private class AfsEntry
        {
            public uint Offset { get; set; }
            public uint Size { get; set; }
        }
    }
}
