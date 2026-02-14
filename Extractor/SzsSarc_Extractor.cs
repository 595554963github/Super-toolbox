using AuroraLib.Compression.Algorithms;
using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class SzsSarc_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] SARC = { 0x53, 0x41, 0x52, 0x43 };
        private static readonly byte[] SFAT = { 0x53, 0x46, 0x41, 0x54 };
        private static readonly byte[] SFNT = { 0x53, 0x46, 0x4E, 0x54 };
        private static readonly byte[] Yaz0 = { 0x59, 0x61, 0x7A, 0x30 };
        private static readonly byte[] FLIM = { 0x46, 0x4C, 0x49, 0x4D };

        private const string NullChar = "\x00";
        private const string Empty = "";
        private const string BFFNT = "bffnt";
        private const uint HashKey = 0x65;
        private const int MinAlignment = 4;

        private static readonly Regex RegexAZ = new Regex("[^a-zA-Z0-9 -]", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> FileExtensions = new() {
            { "AAHS", ".sharc" }, { "AAMP", ".aamp" }, { "BAHS", ".sharcb" },
            { "BNSH", ".bnsh" }, { "BNTX", ".bntx" }, { "BY", ".byaml" },
            { "CFNT", ".bcfnt" }, { "CGFX", ".bcres" }, { "CLAN", ".bclan" },
            { "CLYT", ".bclyt" }, { "CSTM", ".bcstm" }, { "CTPK", ".ctpk" },
            { "CWAV", ".bcwav" }, { "FFNT", ".bffnt" }, { "FLAN", ".bflan" },
            { "FLIM", ".bclim" }, { "FLYT", ".bflyt" }, { "FRES", ".bfres" },
            { "FSEQ", ".bfseq" }, { "FSHA", ".bfsha" }, { "FSTM", ".bfstm" },
            { "FWAV", ".bfwav" }, { "Gfx2", ".gtx" }, { "MsgPrjBn", ".msbp" },
            { "MsgStdBn", ".msbt" }, { "SARC", ".sarc" }, { "STM", ".bfsha" },
            { "VFXB", ".pctl" }, { "Yaz", ".szs" }, { "YB", ".byaml" },
        };

        private enum Endian : ushort { Big = 0xFFFE, Little = 0xFEFF }

        private class SarcArchive : Dictionary<string, byte[]>
        {
            public Endian Endian { get; set; }
            public bool HashOnly { get; set; } = false;
            public bool Legacy { get; set; } = false;

            public SarcArchive(byte[] data)
            {
                using MemoryStream ms = new(data);
                using BinaryReader reader = new(ms);

                byte[] magic = reader.ReadBytes(4);
                if (!magic.SequenceEqual(SARC))
                    throw new InvalidDataException("无效的SARC魔数");

                reader.ReadBytes(2);
                Endian = (Endian)reader.ReadUInt16();

                int fileSize = reader.ReadInt32();
                int dataOffset = reader.ReadInt32();
                reader.ReadBytes(10);

                ushort fileCount = reader.ReadUInt16();
                reader.ReadBytes(4);

                if (Endian == Endian.Big)
                {
                    fileSize = SwapEndian(fileSize);
                    dataOffset = SwapEndian(dataOffset);
                    fileCount = SwapEndian(fileCount);
                }

                var nodes = new List<(uint Hash, int StringOffset, int DataStart, int DataEnd)>();

                for (int i = 0; i < fileCount; i++)
                {
                    uint hash = reader.ReadUInt32();
                    int attributes = reader.ReadInt32();
                    int dataStart = reader.ReadInt32();
                    int dataEnd = reader.ReadInt32();

                    if (Endian == Endian.Big)
                    {
                        hash = SwapEndian(hash);
                        attributes = SwapEndian(attributes);
                        dataStart = SwapEndian(dataStart);
                        dataEnd = SwapEndian(dataEnd);
                    }

                    HashOnly = (byte)(attributes >> 24) != 1;
                    int strOffset = (attributes & 0xFFFF) * 4;

                    nodes.Add((hash, strOffset, dataStart, dataEnd));
                }

                byte[] sfntMagic = reader.ReadBytes(4);
                if (!sfntMagic.SequenceEqual(SFNT))
                    throw new InvalidDataException("无效的SFNT魔数");

                reader.ReadBytes(4);

                if (!HashOnly)
                {
                    int stringTableSize = dataOffset - (int)ms.Position;
                    byte[] stringTable = reader.ReadBytes(stringTableSize);

                    for (int i = 0; i < nodes.Count; i++)
                    {
                        ms.Seek(dataOffset + nodes[i].DataStart, SeekOrigin.Begin);
                        byte[] fileData = reader.ReadBytes(nodes[i].DataEnd - nodes[i].DataStart);

                        string fileName = ReadString(stringTable, nodes[i].StringOffset);
                        Add(fileName, fileData);
                    }
                }
                else
                {
                    ms.Seek(dataOffset, SeekOrigin.Begin);
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        byte[] fileData = reader.ReadBytes(nodes[i].DataEnd - nodes[i].DataStart);
                        string fileName = $"{nodes[i].Hash:x8}.{GuessFileExtension(fileData)}";
                        Add(fileName, fileData);
                    }
                }
            }

            private string ReadString(byte[] stringTable, int offset)
            {
                int end = offset;
                while (end < stringTable.Length && stringTable[end] != 0)
                    end++;
                return Encoding.UTF8.GetString(stringTable, offset, end - offset).Replace(NullChar, Empty);
            }

            private string GuessFileExtension(byte[] data)
            {
                string magic = data.Length >= 8 && data.Take(4).SequenceEqual(Yaz0)
                    ? Encoding.UTF8.GetString(data, 0x11, 4)
                    : data.Length >= 4 ? Encoding.UTF8.GetString(data, 0, 4) : "";

                return FileExtensions.TryGetValue(RegexAZ.Replace(magic, Empty), out string? value) ? value : "bin";
            }

            private int SwapEndian(int value)
            {
                uint unsigned = (uint)value;
                uint swapped = ((unsigned & 0x000000FF) << 24) |
                               ((unsigned & 0x0000FF00) << 8) |
                               ((unsigned & 0x00FF0000) >> 8) |
                               ((unsigned & 0xFF000000) >> 24);
                return (int)swapped;
            }

            private uint SwapEndian(uint value)
            {
                return ((value & 0x000000FF) << 24) |
                       ((value & 0x0000FF00) << 8) |
                       ((value & 0x00FF0000) >> 8) |
                       ((value & 0xFF000000) >> 24);
            }

            private ushort SwapEndian(ushort value)
            {
                return (ushort)(((value & 0x00FF) << 8) | ((value & 0xFF00) >> 8));
            }
        }

        private byte[] DecompressYaz0(byte[] compressedData)
        {
            if (compressedData.Length < 4) return compressedData;
            if (!compressedData.Take(4).SequenceEqual(Yaz0)) return compressedData;

            using var input = new MemoryStream(compressedData);
            using var output = new MemoryStream();
            new Yaz0().Decompress(input, output);
            return output.ToArray();
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
            var archiveFiles = new List<string>();

            ExtractionProgress?.Invoke(this, $"正在扫描文件头,共{allFiles.Count}个文件...");

            foreach (var file in allFiles)
            {
                try
                {
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (fs.Length < 4) continue;

                    byte[] header = new byte[4];
                    await fs.ReadAsync(header, 0, 4, cancellationToken);

                    if (header.SequenceEqual(SARC) || header.SequenceEqual(Yaz0))
                    {
                        archiveFiles.Add(file);
                        ExtractionProgress?.Invoke(this, $"发现sarc/Yaz0文件:{Path.GetFileName(file)}");
                    }
                }
                catch { }
            }

            if (archiveFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何sarc/Yaz0格式的文件");
                OnExtractionFailed("未找到任何sarc/Yaz0格式的文件");
                return;
            }

            TotalFilesToExtract = archiveFiles.Count;
            int processedFiles = 0;
            int totalExtracted = 0;

            try
            {
                foreach (var archiveFile in archiveFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(archiveFile)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        string baseName = Path.GetFileNameWithoutExtension(archiveFile);
                        string outputDir = Path.Combine(Path.GetDirectoryName(archiveFile) ?? "", baseName);
                        Directory.CreateDirectory(outputDir);

                        byte[] fileData = await File.ReadAllBytesAsync(archiveFile, cancellationToken);
                        byte[] dataToProcess = fileData;

                        if (fileData.Length >= 4 && fileData.Take(4).SequenceEqual(Yaz0))
                        {
                            ExtractionProgress?.Invoke(this, $"检测到Yaz0压缩,开始解压:{Path.GetFileName(archiveFile)}");
                            dataToProcess = DecompressYaz0(fileData);
                        }

                        if (dataToProcess.Length >= 4 && dataToProcess.Take(4).SequenceEqual(SARC))
                        {
                            try
                            {
                                var archive = new SarcArchive(dataToProcess);
                                int extractedCount = 0;

                                foreach (var entry in archive)
                                {
                                    ThrowIfCancellationRequested(cancellationToken);
                                    string fileName = entry.Key;
                                    string outputPath = Path.Combine(outputDir, fileName);

                                    string? fileDir = Path.GetDirectoryName(outputPath);
                                    if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                                        Directory.CreateDirectory(fileDir);

                                    outputPath = await GenerateUniqueFilePathAsync(outputPath, cancellationToken);
                                    await File.WriteAllBytesAsync(outputPath, entry.Value, cancellationToken);

                                    extractedCount++;
                                    totalExtracted++;
                                    OnFileExtracted(outputPath);
                                }

                                ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(archiveFile)}提取完成,共{extractedCount}个文件");
                            }
                            catch (Exception ex)
                            {
                                ExtractionError?.Invoke(this, $"解析SARC文件{Path.GetFileName(archiveFile)}时出错:{ex.Message}");
                            }
                        }
                        else
                        {
                            string outputPath = Path.Combine(outputDir, $"{baseName}_decompressed.bin");
                            outputPath = await GenerateUniqueFilePathAsync(outputPath, cancellationToken);
                            await File.WriteAllBytesAsync(outputPath, dataToProcess, cancellationToken);
                            totalExtracted++;
                            OnFileExtracted(outputPath);
                            ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(archiveFile)}解压完成");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(archiveFile)}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,共提取{totalExtracted}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
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
    }
}
