using System.IO.Compression;

namespace super_toolbox
{
    public class Attack_on_Titan_Wings_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const int BLOCK_SIZE = 0x800; // 2048
        private static readonly byte[] G1T_SIGNATURE = { 0x47, 0x54, 0x31, 0x47 };
        private static readonly byte[] G1M_SIGNATURE = { 0x5F, 0x4D, 0x31, 0x47 };
        private static readonly byte[] G1A_SIGNATURE = { 0x5F, 0x41, 0x31, 0x47 };
        private static readonly byte[] KTSL2ASBIN_SIGNATURE = { 0x4B, 0x54, 0x53, 0x52, 0x77, 0x7B, 0x48, 0x1A };
        private static readonly byte[] KTSC_SIGNATURE = { 0x4B, 0x54, 0x53, 0x43 };
        private static readonly byte[] SWG_SIGNATURE = { 0x53, 0x57, 0x47, 0x51 };
        private static readonly byte[] G1EM_SIGNATURE = { 0x4D, 0x45, 0x31, 0x47 };
        private static readonly byte[] WMV_SIGNATURE = { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11 };
        private static readonly byte[] G2S_SIGNATURE = { 0x5F, 0x53, 0x32, 0x47, 0x33, 0x30, 0x30, 0x30 };
        private static readonly byte[] KSHL_SIGNATURE = { 0x4C, 0x48, 0x53, 0x4B, 0x37, 0x31, 0x31, 0x30 };
        private static readonly byte[] SLO_SIGNATURE = { 0x5F, 0x53, 0x32, 0x47, 0x33, 0x30, 0x30, 0x30 };
        private static readonly byte[] KPS_SIGNATURE = { 0x33, 0x53, 0x50, 0x4B, 0x31, 0x30, 0x30, 0x30 };
        private static readonly byte[] CBXD_SIGNATURE = { 0x44, 0x58, 0x42, 0x43 };
        private static readonly byte[] KHM_SIGNATURE = { 0x5F, 0x4D, 0x48, 0x4B, 0x30, 0x31, 0x30, 0x30 };
        private static readonly byte[] ZLIB_HEADER = { 0x78, 0xDA };

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

            try
            {
                string extractedDir = Path.Combine(directoryPath, "Extracted");
                Directory.CreateDirectory(extractedDir);

                var files = Directory.EnumerateFiles(directoryPath, "*.bin", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.Contains("Extracted", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count == 0)
                {
                    ExtractionError?.Invoke(this, "没有找到BIN文件");
                    OnExtractionFailed("没有找到BIN文件");
                    return;
                }
                TotalFilesToExtract = files.Count;
                ExtractionStarted?.Invoke(this, $"开始处理{files.Count}个BIN文件");
                int totalEntries = await CalculateTotalEntriesAsync(files, cancellationToken);
                ExtractionProgress?.Invoke(this, $"预计提取{totalEntries}个文件");

                await ProcessBinFilesAsync(files, extractedDir, cancellationToken);
                var datFiles = Directory.EnumerateFiles(extractedDir, "*.dat", SearchOption.AllDirectories).ToList();
                if (datFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理{datFiles.Count}个DAT文件");
                    await ProcessDatFilesAsync(datFiles, extractedDir, cancellationToken);
                }

                var zlibFiles = Directory.EnumerateFiles(extractedDir, "*.zlib", SearchOption.AllDirectories).ToList();
                if (zlibFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理{zlibFiles.Count}个ZLIB文件");
                    await ProcessZlibFilesAsync(zlibFiles, cancellationToken);
                }

                ExtractionProgress?.Invoke(this, $"提取完成:共处理{files.Count}个BIN文件，提取出{ExtractedFileCount}个文件");
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
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        private async Task<int> CalculateTotalEntriesAsync(List<string> binFiles, CancellationToken ct)
        {
            int total = 0;
            foreach (var filePath in binFiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var reader = new BinaryReader(fs);
                    var header = ReadFileHeader(reader);
                    total += header.FileCount;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"扫描文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                }
            }
            return total;
        }

        private async Task ProcessBinFilesAsync(List<string> binFiles, string extractedDir, CancellationToken ct)
        {
            await Parallel.ForEachAsync(binFiles, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            }, async (filePath, ct) =>
            {
                try
                {
                    ExtractionProgress?.Invoke(this, $"正在处理BIN文件:{Path.GetFileName(filePath)}");

                    await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var reader = new BinaryReader(fs);
                    var header = ReadFileHeader(reader);
                    var entries = ReadFileEntries(reader, header.FileCount).ToList();

                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string outputDir = Path.Combine(extractedDir, baseName);
                    Directory.CreateDirectory(outputDir);

                    await ProcessEntriesAsync(reader, entries, outputDir, ct);
                    OnFileExtracted(filePath);
                    ExtractionProgress?.Invoke(this, $"完成处理BIN文件:{Path.GetFileName(filePath)}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理BIN文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                    OnExtractionFailed($"处理BIN文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                }
            });
        }

        private async Task ProcessZlibFilesAsync(List<string> zlibFiles, CancellationToken ct)
        {
            await Parallel.ForEachAsync(zlibFiles, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            }, async (zlibFilePath, ct) =>
            {
                try
                {
                    string newFilePath = Path.ChangeExtension(zlibFilePath, ".zlib");
                    await Task.Run(() => File.Move(zlibFilePath, newFilePath), ct);
                    ExtractionProgress?.Invoke(this, $"已重命名文件:{Path.GetFileName(zlibFilePath)} -> {Path.GetFileName(newFilePath)}");
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理ZLIB文件{Path.GetFileName(zlibFilePath)}时出错:{ex.Message}");
                }
            });
        }

        private async Task ProcessDatFilesAsync(List<string> datFiles, string baseOutputDir, CancellationToken ct)
        {
            await Parallel.ForEachAsync(datFiles, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            }, async (datFilePath, ct) =>
            {
                try
                {
                    byte[] fileData = await File.ReadAllBytesAsync(datFilePath, ct);

                    if (fileData.Length >= 10 &&
                        ((fileData[0] == 0x78 && fileData[1] == 0xDA) ||
                         (fileData[8] == 0x78 && fileData[9] == 0xDA)))
                    {
                        string newFilePath = Path.ChangeExtension(datFilePath, ".zlib");
                        File.Move(datFilePath, newFilePath);
                        ExtractionProgress?.Invoke(this, $"已重命名文件:{Path.GetFileName(datFilePath)} -> {Path.GetFileName(newFilePath)}");
                        return;
                    }

                    ExtractionProgress?.Invoke(this, $"保留文件(未处理):{Path.GetFileName(datFilePath)}");
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理DAT文件{Path.GetFileName(datFilePath)}时出错:{ex.Message}");
                }
            });
        }

        private record FileHeader(int Magic, int FileCount, int Type, int Blank);
        private record FileEntry(long Position, int Size, int Compression);

        private static FileHeader ReadFileHeader(BinaryReader reader)
        {
            return new FileHeader(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            );
        }

        private static IEnumerable<FileEntry> ReadFileEntries(BinaryReader reader, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new FileEntry(
                    reader.ReadInt64() * BLOCK_SIZE,
                    reader.ReadInt32(),
                    reader.ReadInt32()
                );
            }
        }

        private async Task ProcessEntriesAsync(BinaryReader reader, List<FileEntry> entries, string outputDir, CancellationToken ct)
        {
            int fileIndex = 1;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    reader.BaseStream.Position = entry.Position;
                    byte[] data = reader.ReadBytes(entry.Size);
                    string extension = DetectFileExtension(data);
                    string outputPath = Path.Combine(outputDir, $"{fileIndex}{extension}");

                    if (entry.Compression != 0)
                    {
                        data = await DecompressDataAsync(data, ct);
                    }

                    await File.WriteAllBytesAsync(outputPath, data, ct);
                    OnFileExtracted(outputPath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
                    fileIndex++;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理条目{entry.Position}时出错:{ex.Message}");
                    OnExtractionFailed($"处理条目{entry.Position}时出错:{ex.Message}");
                }
            }
        }

        private static async Task<byte[]> DecompressDataAsync(byte[] compressedData, CancellationToken ct)
        {
            try
            {
                await using var input = new MemoryStream(compressedData);
                await using var output = new MemoryStream();
                if (compressedData.Length >= 2 && compressedData[0] == 0x78 && compressedData[1] == 0xDA)
                {
                    await using var zlib = new ZLibStream(input, CompressionMode.Decompress, true);
                    await zlib.CopyToAsync(output, ct);
                    return output.ToArray();
                }
                return compressedData;
            }
            catch (Exception)
            {
                return compressedData;
            }
        }
        private static string DetectFileExtension(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4) return ".dat";

            if (data.Length >= G1T_SIGNATURE.Length && data[..G1T_SIGNATURE.Length].SequenceEqual(G1T_SIGNATURE))
                return ".g1t";

            if (data.Length >= G1M_SIGNATURE.Length && data[..G1M_SIGNATURE.Length].SequenceEqual(G1M_SIGNATURE))
                return ".g1m";

            if (data.Length >= G1A_SIGNATURE.Length && data[..G1A_SIGNATURE.Length].SequenceEqual(G1A_SIGNATURE))
                return ".g1a";

            if (data.Length >= KTSL2ASBIN_SIGNATURE.Length && data[..KTSL2ASBIN_SIGNATURE.Length].SequenceEqual(KTSL2ASBIN_SIGNATURE))
                return ".ktsl2asbin";

            if (data.Length >= KTSC_SIGNATURE.Length && data[..KTSC_SIGNATURE.Length].SequenceEqual(KTSC_SIGNATURE))
                return ".ktsc";

            if (data.Length >= SWG_SIGNATURE.Length && data[..SWG_SIGNATURE.Length].SequenceEqual(SWG_SIGNATURE))
                return ".swg";

            if (data.Length >= 2 && data[0] == 0x78 && (data[1] == 0x9C || data[1] == 0xDA || data[1] == 0x01))
                return ".zlib";

            if (data.Length >= WMV_SIGNATURE.Length && data[..WMV_SIGNATURE.Length].SequenceEqual(WMV_SIGNATURE))
                return ".wmv";

            if (data.Length >= G1EM_SIGNATURE.Length && data[..G1EM_SIGNATURE.Length].SequenceEqual(G1EM_SIGNATURE))
                return ".g1em";

            if (data.Length >= G2S_SIGNATURE.Length && data[..G2S_SIGNATURE.Length].SequenceEqual(G2S_SIGNATURE))
                return ".g2s";

            if (data.Length >= KSHL_SIGNATURE.Length && data[..KSHL_SIGNATURE.Length].SequenceEqual(KSHL_SIGNATURE))
                return ".kshl";

            if (data.Length >= SLO_SIGNATURE.Length && data[..SLO_SIGNATURE.Length].SequenceEqual(SLO_SIGNATURE))
                return ".slod";

            if (data.Length >= KPS_SIGNATURE.Length && data[..KPS_SIGNATURE.Length].SequenceEqual(KPS_SIGNATURE))
                return ".kps";

            if (data.Length >= CBXD_SIGNATURE.Length && data[..CBXD_SIGNATURE.Length].SequenceEqual(CBXD_SIGNATURE))
                return ".cbxd";

            if (data.Length >= KHM_SIGNATURE.Length && data[..KHM_SIGNATURE.Length].SequenceEqual(KHM_SIGNATURE))
                return ".khm";

            return ".dat";
        }
    }
}
