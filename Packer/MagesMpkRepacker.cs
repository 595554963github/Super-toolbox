using System.Text;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class MagesMpkRepacker : BaseExtractor
    {
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        private const uint MPK_MAGIC = 0x004B504D;
        private const uint MPK_VERSION = 0x020000;
        private const int ALIGNMENT = 2048;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MpkHeader
        {
            public uint magic;
            public uint version;
            public ulong entries;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x30)]
            public byte[] padding;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MpkEntry
        {
            public uint compression;
            public uint entry_id;
            public ulong offset;
            public ulong size;
            public ulong size_decompressed;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0xE0)]
            public byte[] filename;
        }

        private class EntryInfo
        {
            public MpkEntry Entry { get; set; }
            public string FilePath { get; set; } = string.Empty;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await RepackAsync(directoryPath, cancellationToken);
        }

        public async Task RepackAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnPackingFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                                   .Where(f => !f.EndsWith(".mpk", StringComparison.OrdinalIgnoreCase))
                                   .ToList();

            if (allFiles.Count == 0)
            {
                PackingError?.Invoke(this, "未找到可打包的文件");
                OnPackingFailed("未找到可打包的文件");
                return;
            }

            TotalFilesToPack = allFiles.Count;
            allFiles.Sort();

            PackingStarted?.Invoke(this, $"开始打包{allFiles.Count}个文件到MPK文件");
            PackingProgress?.Invoke(this, $"找到{allFiles.Count}个文件");

            try
            {
                string parentDirectory = Path.GetDirectoryName(directoryPath) ?? directoryPath;
                string mpkFileName = Path.GetFileName(directoryPath) + ".mpk";
                string outputPath = Path.Combine(parentDirectory, mpkFileName);

                await CreateMpkFile(directoryPath, allFiles, outputPath, cancellationToken);
                OnPackingCompleted();
            }
            catch (OperationCanceledException)
            {
                PackingError?.Invoke(this, "打包操作已取消");
                OnPackingFailed("打包操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"打包失败:{ex.Message}");
                OnPackingFailed($"打包失败:{ex.Message}");
            }
        }

        private async Task CreateMpkFile(string baseDirectory, List<string> files, string outputPath, CancellationToken cancellationToken)
        {
            var entries = new List<EntryInfo>();

            for (uint i = 0; i < files.Count; i++)
            {
                var entry = new MpkEntry
                {
                    compression = 0,
                    entry_id = i,
                    offset = 0,
                    size = 0,
                    size_decompressed = 0,
                    filename = new byte[0xE0]
                };

                string relativePath = files[(int)i];
                if (relativePath.StartsWith(baseDirectory))
                {
                    relativePath = relativePath.Substring(baseDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }

                relativePath = relativePath.Replace('\\', '/');
                byte[] filenameBytes = Encoding.ASCII.GetBytes(relativePath);
                int copyLen = Math.Min(filenameBytes.Length, 0xDF);
                Array.Copy(filenameBytes, entry.filename, copyLen);
                entry.filename[copyLen] = 0;

                entries.Add(new EntryInfo { Entry = entry, FilePath = files[(int)i] });
            }

            string outputDir = Path.GetDirectoryName(outputPath) ?? string.Empty;
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                var header = new MpkHeader
                {
                    magic = MPK_MAGIC,
                    version = MPK_VERSION,
                    entries = (ulong)entries.Count,
                    padding = new byte[0x30]
                };

                WriteMpkHeader(bw, header);

                long entriesOffset = fs.Position;
                for (int i = 0; i < entries.Count; i++)
                {
                    WriteMpkEntry(bw, new MpkEntry());
                }

                long currentPos = fs.Position;
                long alignedPos = AlignUp(currentPos, ALIGNMENT);
                if (alignedPos > currentPos)
                {
                    byte[] padding = new byte[alignedPos - currentPos];
                    await fs.WriteAsync(padding, 0, padding.Length, cancellationToken);
                }

                byte[] buffer = new byte[64 * 1024 * 1024];

                for (int i = 0; i < entries.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entryInfo = entries[i];
                    var entry = entryInfo.Entry;
                    long fileOffset = fs.Position;
                    entry.offset = (ulong)fileOffset;

                    using (var fileFs = new FileStream(entryInfo.FilePath, FileMode.Open, FileAccess.Read))
                    {
                        long fileSize = fileFs.Length;
                        entry.size = entry.size_decompressed = (ulong)fileSize;

                        ulong remaining = entry.size;
                        ulong totalWritten = 0;

                        while (remaining > 0)
                        {
                            int chunkSize = (int)Math.Min(remaining, (ulong)buffer.Length);
                            int bytesRead = await fileFs.ReadAsync(buffer, 0, chunkSize, cancellationToken);

                            if (bytesRead == 0)
                                break;

                            await fs.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            totalWritten += (ulong)bytesRead;
                            remaining -= (ulong)bytesRead;
                        }

                        if (totalWritten != entry.size)
                        {
                            PackingError?.Invoke(this, $"警告:文件大小不匹配:{entryInfo.FilePath}(预期:{entry.size},实际:{totalWritten})");
                            entry.size = totalWritten;
                        }
                    }

                    entryInfo.Entry = entry;
                    OnFilePacked(entryInfo.FilePath);
                    PackingProgress?.Invoke(this, $"正在打包:{Path.GetFileName(entryInfo.FilePath)}");

                    if (i < entries.Count - 1)
                    {
                        currentPos = fs.Position;
                        alignedPos = AlignUp(currentPos, ALIGNMENT);
                        if (alignedPos > currentPos)
                        {
                            byte[] padding = new byte[alignedPos - currentPos];
                            await fs.WriteAsync(padding, 0, padding.Length, cancellationToken);
                        }
                    }

                    if (i % 100 == 0 || i == entries.Count - 1)
                    {
                        PackingProgress?.Invoke(this, $"写入文件数据{i + 1}/{entries.Count}(偏移:0x{fileOffset:X},大小:{entry.size})");
                    }
                }

                fs.Seek(entriesOffset, SeekOrigin.Begin);
                for (int i = 0; i < entries.Count; i++)
                {
                    WriteMpkEntry(bw, entries[i].Entry);
                }

                FileInfo fileInfo = new FileInfo(outputPath);
                PackingProgress?.Invoke(this, $"打包完成:{Path.GetFileName(outputPath)}(包含{entries.Count}个文件,大小:{FormatFileSize(fileInfo.Length)})");
            }
        }

        private void WriteMpkHeader(BinaryWriter bw, MpkHeader header)
        {
            bw.Write(header.magic);
            bw.Write(header.version);
            bw.Write(header.entries);
            if (header.padding != null)
            {
                bw.Write(header.padding);
            }
            else
            {
                bw.Write(new byte[0x30]);
            }
        }

        private void WriteMpkEntry(BinaryWriter bw, MpkEntry entry)
        {
            bw.Write(entry.compression);
            bw.Write(entry.entry_id);
            bw.Write(entry.offset);
            bw.Write(entry.size);
            bw.Write(entry.size_decompressed);
            if (entry.filename != null)
            {
                bw.Write(entry.filename);
            }
            else
            {
                bw.Write(new byte[0xE0]);
            }
        }

        private long AlignUp(long size, long alignment)
        {
            return (size + alignment - 1) & ~(alignment - 1);
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        public void Repack(string directoryPath)
        {
            RepackAsync(directoryPath).Wait();
        }

        public override void Extract(string directoryPath)
        {
            Repack(directoryPath);
        }
    }
}
