using System.Text;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class MagesMpkExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const uint MPK_MAGIC = 0x004B504D;

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
            var mpkFiles = Directory.GetFiles(directoryPath, "*.mpk", SearchOption.AllDirectories);
            if (mpkFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.mpk文件");
                OnExtractionFailed("未找到任何.mpk文件");
                return;
            }

            TotalFilesToExtract = mpkFiles.Length;
            int processedFiles = 0;

            try
            {
                foreach (var mpkFilePath in mpkFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(mpkFilePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        await ExtractSingleMpkFile(mpkFilePath, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{mpkFilePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{mpkFilePath}时出错:{ex.Message}");
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

        private async Task ExtractSingleMpkFile(string mpkFilePath, CancellationToken cancellationToken)
        {
            string fileDirectory = Path.GetDirectoryName(mpkFilePath) ?? string.Empty;
            string mpkFileNameWithoutExt = Path.GetFileNameWithoutExtension(mpkFilePath);
            string extractedFolder = Path.Combine(fileDirectory, mpkFileNameWithoutExt);

            if (Directory.Exists(extractedFolder))
                Directory.Delete(extractedFolder, true);
            Directory.CreateDirectory(extractedFolder);

            using (var fs = new FileStream(mpkFilePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                MpkHeader header = ReadMpkHeader(br);

                if (header.magic != MPK_MAGIC)
                {
                    ExtractionError?.Invoke(this, $"{Path.GetFileName(mpkFilePath)}无效的MPK文件");
                    return;
                }

                var entries = new MpkEntry[header.entries];
                for (ulong i = 0; i < header.entries; i++)
                {
                    entries[i] = ReadMpkEntry(br);
                }

                for (uint i = 0; i < entries.Length; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    var entry = entries[i];

                    if (entry.offset == 0 || entry.offset == ulong.MaxValue)
                    {
                        ExtractionError?.Invoke(this, $"文件{i}的偏移量无效:{entry.offset}");
                        continue;
                    }

                    string outputFilename = Encoding.ASCII.GetString(entry.filename).TrimEnd('\0');
                    if (string.IsNullOrEmpty(outputFilename))
                    {
                        outputFilename = $"file_{i + 1}.bin";
                    }

                    var outputPath = Path.Combine(extractedFolder, outputFilename);
                    var outputDir = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }
                    if (entry.size == 0)
                    {
                        File.Create(outputPath).Close();
                        OnFileExtracted(outputPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
                        continue;
                    }

                    fs.Seek((long)entry.offset, SeekOrigin.Begin);

                    using (var outputFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                    {
                        ulong remaining = entry.size;
                        byte[] buffer = new byte[64 * 1024 * 1024];

                        while (remaining > 0)
                        {
                            ThrowIfCancellationRequested(cancellationToken);

                            int chunkSize = (int)Math.Min(remaining, (ulong)buffer.Length);
                            byte[] readBuffer = br.ReadBytes(chunkSize);

                            if (readBuffer.Length == 0)
                            {
                                ExtractionError?.Invoke(this, $"读取MPK文件数据失败(文件{i})");
                                break;
                            }

                            await outputFs.WriteAsync(readBuffer, 0, readBuffer.Length, cancellationToken);
                            remaining -= (ulong)readBuffer.Length;
                        }
                    }

                    OnFileExtracted(outputPath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");

                    if (i % 100 == 0 || i == entries.Length - 1)
                    {
                        ExtractionProgress?.Invoke(this, $"解包文件{i + 1}/{entries.Length}: {outputFilename}");
                    }
                }
            }

            ExtractionProgress?.Invoke(this, $"{Path.GetFileName(mpkFilePath)}解包完成");
        }

        private MpkHeader ReadMpkHeader(BinaryReader br)
        {
            var header = new MpkHeader();
            header.magic = br.ReadUInt32();
            header.version = br.ReadUInt32();
            header.entries = br.ReadUInt64();
            header.padding = br.ReadBytes(0x30);
            return header;
        }

        private MpkEntry ReadMpkEntry(BinaryReader br)
        {
            var entry = new MpkEntry();
            entry.compression = br.ReadUInt32();
            entry.entry_id = br.ReadUInt32();
            entry.offset = br.ReadUInt64();
            entry.size = br.ReadUInt64();
            entry.size_decompressed = br.ReadUInt64();
            entry.filename = br.ReadBytes(0xE0);
            return entry;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
