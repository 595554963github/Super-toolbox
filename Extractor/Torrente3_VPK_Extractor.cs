using System.Text;

namespace super_toolbox
{
    public class Torrente3_VPK_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] VPK_SIGNATURE = { 0x76, 0x74, 0x50, 0x61, 0x63, 0x6B };
        private const uint INVALID_OFFSET = uint.MaxValue;
        private int _extractedFileCounter;

        public override void Extract(string path)
        {
            ExtractAsync(path).Wait();
        }

        public override async Task ExtractAsync(string path, CancellationToken cancellationToken = default)
        {
            if (File.Exists(path))
            {
                await ProcessSingleFileAsync(path, cancellationToken);
            }
            else if (Directory.Exists(path))
            {
                await ProcessDirectoryAsync(path, cancellationToken);
            }
            else
            {
                ExtractionError?.Invoke(this, $"路径不存在:{path}");
                OnExtractionFailed($"路径不存在:{path}");
            }
        }

        private async Task ProcessDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
        {
            var filePaths = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath},共找到{filePaths.Count}个VPK文件");

            int processedFiles = 0;
            _extractedFileCounter = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");
                    try
                    {
                        await ProcessVpkFileAsync(filePath, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,共提取{_extractedFileCounter}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ProcessSingleFileAsync(string filePath, CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(filePath);
            ExtractionStarted?.Invoke(this, $"开始处理单个文件:{fileName}");
            TotalFilesToExtract = 1;
            _extractedFileCounter = 0;

            try
            {
                await ProcessVpkFileAsync(filePath, cancellationToken);
                ExtractionProgress?.Invoke(this, $"处理完成,从{fileName}中提取出{_extractedFileCounter}个文件");
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
                ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
                OnExtractionFailed($"处理文件{fileName}时出错:{ex.Message}");
                throw;
            }
        }

        private async Task ProcessVpkFileAsync(string vpkFilePath, CancellationToken cancellationToken)
        {
            string baseName = Path.GetFileNameWithoutExtension(vpkFilePath);
            string outputDir = Path.Combine(Path.GetDirectoryName(vpkFilePath) ?? "", $"{baseName}");
            Directory.CreateDirectory(outputDir);

            using var fs = new FileStream(vpkFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (!await VerifySignatureAsync(fs, cancellationToken))
            {
                ExtractionError?.Invoke(this, $"文件{Path.GetFileName(vpkFilePath)}不是有效的vtPack格式(签名不匹配)");
                return;
            }

            uint version = await ReadUInt32LittleEndianAsync(fs, cancellationToken);
            uint unk1 = await ReadUInt32LittleEndianAsync(fs, cancellationToken);
            uint unk2 = await ReadUInt32LittleEndianAsync(fs, cancellationToken);

            if (version == 1)
            {
                _ = await ReadUInt32LittleEndianAsync(fs, cancellationToken);
                _ = await ReadUInt32LittleEndianAsync(fs, cancellationToken);
            }
            else if (version == 2)
            {
                _ = await ReadUInt64LittleEndianAsync(fs, cancellationToken);
                _ = await ReadUInt64LittleEndianAsync(fs, cancellationToken);
            }
            else
            {
                ExtractionError?.Invoke(this, $"文件{Path.GetFileName(vpkFilePath)}不支持的版本:{version}(仅支持1/2)");
                return;
            }

            uint entryCount = await ReadUInt32LittleEndianAsync(fs, cancellationToken);
            ulong strTableAbsOffset = version == 1 ? await ReadUInt32LittleEndianAsync(fs, cancellationToken) : await ReadUInt64LittleEndianAsync(fs, cancellationToken);

            if (strTableAbsOffset >= (ulong)fs.Length)
            {
                ExtractionError?.Invoke(this, $"文件{Path.GetFileName(vpkFilePath)}格式错误:字符串表偏移{strTableAbsOffset}超出文件范围");
                return;
            }

            fs.Seek((long)strTableAbsOffset, SeekOrigin.Begin);
            uint strTableSize = await ReadUInt32LittleEndianAsync(fs, cancellationToken);
            if (strTableSize == 0 || (ulong)fs.Position + strTableSize > (ulong)fs.Length)
            {
                ExtractionError?.Invoke(this, $"文件{Path.GetFileName(vpkFilePath)}格式错误:字符串表大小{strTableSize}无效/超出文件范围");
                return;
            }

            byte[] strTableData = new byte[strTableSize];
            await fs.ReadAsync(strTableData, 0, (int)strTableSize, cancellationToken);
            var stringCache = ParseStringTable(strTableData);

            var entries = new List<VtPackRawEntryHeader>();
            for (int i = 0; i < entryCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = new VtPackRawEntryHeader
                {
                    PathNameStrTableOffset = await ReadUInt32LittleEndianAsync(fs, cancellationToken),
                    PathDirStrTableOffset = await ReadUInt32LittleEndianAsync(fs, cancellationToken),
                    Unk1 = await ReadUInt32LittleEndianAsync(fs, cancellationToken),
                    FileSize = await ReadUInt64LittleEndianAsync(fs, cancellationToken),
                    Unk2 = await ReadUInt64LittleEndianAsync(fs, cancellationToken),
                    FileDataAbsOffset = await ReadUInt64LittleEndianAsync(fs, cancellationToken),
                    Unk3 = await ReadUInt32LittleEndianAsync(fs, cancellationToken),
                    Unk4 = await ReadUInt32LittleEndianAsync(fs, cancellationToken)
                };
                entries.Add(entry);
            }

            int extractedInThisFile = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessAndExportEntryAsync(fs, entry, stringCache, outputDir, cancellationToken);
                extractedInThisFile++;
            }

            ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(vpkFilePath)}处理完成,提取{extractedInThisFile}个条目到{outputDir}文件夹");
        }

        private async Task<bool> VerifySignatureAsync(Stream stream, CancellationToken cancellationToken)
        {
            stream.Seek(0, SeekOrigin.Begin);
            byte[] signature = new byte[VPK_SIGNATURE.Length];
            int read = await stream.ReadAsync(signature, 0, signature.Length, cancellationToken);
            if (read != signature.Length) return false;
            for (int i = 0; i < signature.Length; i++)
            {
                if (signature[i] != VPK_SIGNATURE[i]) return false;
            }
            return true;
        }

        private Dictionary<uint, string> ParseStringTable(byte[] strTableData)
        {
            var cache = new Dictionary<uint, string>();
            uint offset = 0;
            int len = strTableData.Length;
            while (offset < len)
            {
                uint currentOffset = offset;
                int strEnd = Array.IndexOf(strTableData, (byte)0, (int)offset);
                if (strEnd == -1) break;
                if (strEnd > offset)
                {
                    string str = Encoding.UTF8.GetString(strTableData, (int)offset, strEnd - (int)offset)
                        .Replace('\0', ' ');
                    cache[currentOffset] = str;
                }
                offset = (uint)strEnd + 1;
            }
            return cache;
        }

        private async Task ProcessAndExportEntryAsync(FileStream fs, VtPackRawEntryHeader entry,
            Dictionary<uint, string> stringCache, string outputRoot, CancellationToken cancellationToken)
        {
            string dirStr = entry.PathDirStrTableOffset != INVALID_OFFSET && stringCache.ContainsKey(entry.PathDirStrTableOffset)
                ? stringCache[entry.PathDirStrTableOffset]
                : string.Empty;
            string nameStr = entry.PathNameStrTableOffset != INVALID_OFFSET && stringCache.ContainsKey(entry.PathNameStrTableOffset)
                ? stringCache[entry.PathNameStrTableOffset]
                : string.Empty;

            string rawPath = $@"{dirStr}\{nameStr}".Replace(@"\\", @"\");
            rawPath = rawPath.TrimStart('\\', '/', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(rawPath)) rawPath = "unnamed_entry";

            string safePath = SanitizePath(rawPath);
            string fullOutputPath = Path.Combine(outputRoot, safePath);
            bool isFile = entry.FileDataAbsOffset != 0;

            if (!isFile)
            {
                Directory.CreateDirectory(fullOutputPath);
                return;
            }

            if (entry.FileSize == 0 || entry.FileDataAbsOffset + entry.FileSize > (ulong)fs.Length)
            {
                ExtractionError?.Invoke(this, $"条目{safePath}数据无效(偏移:{entry.FileDataAbsOffset},大小:{entry.FileSize}),跳过");
                return;
            }

            try
            {
                string fileDir = Path.GetDirectoryName(fullOutputPath) ?? outputRoot;
                Directory.CreateDirectory(fileDir);
                string uniquePath = GetUniqueFilePath(fullOutputPath);

                fs.Seek((long)entry.FileDataAbsOffset, SeekOrigin.Begin);
                using var outFs = new FileStream(uniquePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
                await CopyStreamAsync(fs, outFs, entry.FileSize, cancellationToken);

                _extractedFileCounter++;
                OnFileExtracted(uniquePath);
                ExtractionProgress?.Invoke(this, $"已提取:{safePath}");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取条目{safePath}时出错:{ex.Message}");
            }
        }

        private async Task CopyStreamAsync(Stream source, Stream destination, ulong length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4096];
            ulong remaining = length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int readSize = (int)Math.Min(remaining, (ulong)buffer.Length);
                int read = await source.ReadAsync(buffer, 0, readSize, cancellationToken);
                if (read == 0) break;
                await destination.WriteAsync(buffer, 0, read, cancellationToken);
                remaining -= (ulong)read;
            }
        }

        private async Task<uint> ReadUInt32LittleEndianAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] data = new byte[4];
            int read = await stream.ReadAsync(data, 0, 4, cancellationToken);
            if (read != 4) throw new EndOfStreamException("读取u32时到达流末尾");
            if (BitConverter.IsLittleEndian) return BitConverter.ToUInt32(data, 0);
            Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        private async Task<ulong> ReadUInt64LittleEndianAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] data = new byte[8];
            int read = await stream.ReadAsync(data, 0, 8, cancellationToken);
            if (read != 8) throw new EndOfStreamException("读取u64时到达流末尾");
            if (BitConverter.IsLittleEndian) return BitConverter.ToUInt64(data, 0);
            Array.Reverse(data);
            return BitConverter.ToUInt64(data, 0);
        }

        private string SanitizePath(string path)
        {
            var invalidChars = new HashSet<char>(Path.GetInvalidPathChars());
            invalidChars.UnionWith(Path.GetInvalidFileNameChars());
            StringBuilder sb = new StringBuilder();
            foreach (char c in path)
            {
                sb.Append(invalidChars.Contains(c) ? '_' : c);
            }
            return sb.ToString().Replace(@"\\", @"\");
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath)) return filePath;
            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(filePath);
            string ext = Path.GetExtension(filePath);
            int counter = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name}_{counter}{ext}");
                counter++;
            } while (File.Exists(newPath));
            return newPath;
        }

        private struct VtPackRawEntryHeader
        {
            public uint PathNameStrTableOffset;
            public uint PathDirStrTableOffset;
            public uint Unk1;
            public ulong FileSize;
            public ulong Unk2;
            public ulong FileDataAbsOffset;
            public uint Unk3;
            public uint Unk4;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            base.ThrowIfCancellationRequested(cancellationToken);
        }
    }
}