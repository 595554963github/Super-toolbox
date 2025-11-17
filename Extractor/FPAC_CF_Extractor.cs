using System.Text;
using System.IO.Compression;

namespace super_toolbox
{
    public class FPAC_CF_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] PAC_PREFIX = new byte[] { 0x46, 0x50, 0x41, 0x43 };
        private static readonly byte[] COMPRESSED_PAC_PREFIX = new byte[] { 0x44, 0x46, 0x41, 0x53, 0x46, 0x50, 0x41, 0x43 };
        private const int PAC_HEADER_SIZE = 32;
        private const int INT_SIZE = 4;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:选择的目录不存在");
                OnExtractionFailed("错误:选择的目录不存在");
                return;
            }

            var pacFiles = Directory.GetFiles(directoryPath, "*.pac", SearchOption.AllDirectories);
            if (pacFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.pac文件");
                OnExtractionFailed("未找到任何.pac文件");
                return;
            }

            TotalFilesToExtract = pacFiles.Length;
            ExtractionStarted?.Invoke(this, $"找到{pacFiles.Length}个PAC文件，开始处理...");

            try
            {
                await Task.Run(() =>
                {
                    var tempFiles = new List<string>();
                    int totalExtractedFiles = 0;
                    int processedCount = 0;

                    foreach (var pacFile in pacFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (IsCompressedPACFile(pacFile))
                        {
                            string? tempFile = DecompressToTempFile(pacFile);
                            if (tempFile != null)
                            {
                                tempFiles.Add(tempFile);
                            }
                        }
                    }

                    var allFilesToProcess = pacFiles.Where(f => !IsCompressedPACFile(f)).Concat(tempFiles).ToList();

                    var parallelOptions = new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    };

                    foreach (var pacFilePath in allFilesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var extractedFiles = ProcessSingleFPACFile(pacFilePath);
                        totalExtractedFiles += extractedFiles.Count;
                        processedCount++;

                        foreach (var extractedFile in extractedFiles)
                        {
                            OnFileExtracted(extractedFile);
                        }
                    }

                    foreach (var tempFile in tempFiles)
                    {
                        try
                        {
                            File.Delete(tempFile);
                            ExtractionProgress?.Invoke(this, $"已删除临时文件:{Path.GetFileName(tempFile)}");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"删除临时文件失败:{Path.GetFileName(tempFile)} - {ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"处理完成，共解包{processedCount}/{pacFiles.Length}个PAC文件，总共提取出{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }

        private bool IsCompressedPACFile(string filePath)
        {
            try
            {
                byte[] header = new byte[8];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fs.Read(header, 0, 8);
                }
                return header.SequenceEqual(COMPRESSED_PAC_PREFIX);
            }
            catch
            {
                return false;
            }
        }

        private string? DecompressToTempFile(string compressedFilePath)
        {
            string fileName = Path.GetFileName(compressedFilePath);
            try
            {
                ExtractionProgress?.Invoke(this, $"正在解压:{fileName}");

                byte[] compressedData = File.ReadAllBytes(compressedFilePath);

                if (!StartsWith(compressedData, COMPRESSED_PAC_PREFIX))
                {
                    ExtractionError?.Invoke(this, $"{fileName}不是有效的压缩FPAC文件");
                    return null;
                }

                int offset = COMPRESSED_PAC_PREFIX.Length;
                int uncompressedSize = ReadInt32(compressedData, ref offset);
                int compressedSize = ReadInt32(compressedData, ref offset);

                byte[] zlibData = new byte[compressedSize];
                Array.Copy(compressedData, offset, zlibData, 0, compressedSize);

                byte[] decompressedData = DecompressZlib(zlibData);

                if (decompressedData.Length != uncompressedSize)
                {
                    throw new Exception($"解压后大小不匹配:期望{uncompressedSize}, 实际{decompressedData.Length}");
                }

                string tempFilePath = Path.Combine(Path.GetDirectoryName(compressedFilePath) ?? Directory.GetCurrentDirectory(),
                    Path.GetFileNameWithoutExtension(compressedFilePath) + "_decompressed.pac");

                File.WriteAllBytes(tempFilePath, decompressedData);
                ExtractionProgress?.Invoke(this, $"{fileName}解压完成");

                return tempFilePath;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"{fileName}解压失败:{ex.Message}");
                return null;
            }
        }

        private byte[] DecompressZlib(byte[] zlibData)
        {
            if (zlibData.Length < 2)
                throw new Exception("zlib数据过短");

            byte cmf = zlibData[0];
            byte flg = zlibData[1];

            if ((cmf & 0x0F) != 8)
                throw new Exception("不支持的压缩方法");

            if (((cmf << 8) + flg) % 31 != 0)
                throw new Exception("zlib头校验失败");

            using (var compressedStream = new MemoryStream(zlibData, 2, zlibData.Length - 2))
            using (var decompressionStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
            using (var resultStream = new MemoryStream())
            {
                decompressionStream.CopyTo(resultStream);
                return resultStream.ToArray();
            }
        }

        private List<string> ProcessSingleFPACFile(string pacFilePath)
        {
            string fileName = Path.GetFileName(pacFilePath);
            var extractedFiles = new List<string>();

            try
            {
                ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                byte[] pacData = File.ReadAllBytes(pacFilePath);

                if (!StartsWith(pacData, PAC_PREFIX))
                {
                    ExtractionError?.Invoke(this, $"{fileName}不是有效的FPAC文件，跳过");
                    return extractedFiles;
                }

                extractedFiles = ExtractFromPacData(pacFilePath, pacData);
                ExtractionProgress?.Invoke(this, $"{fileName}处理完成，提取出{extractedFiles.Count}个文件");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"{fileName} 处理失败:{ex.Message}");
            }

            return extractedFiles;
        }

        private List<string> ExtractFromPacData(string pacFilePath, byte[] pacContents)
        {
            var extractedFiles = new List<string>();

            int dataStart, stringSize, fileCount, entrySize;
            byte[] remainingData;
            (dataStart, stringSize, fileCount, entrySize, remainingData) = ParseHeader(pacContents);

            if (fileCount == 0)
            {
                ExtractionProgress?.Invoke(this, $"{Path.GetFileName(pacFilePath)}不包含任何文件");
                return extractedFiles;
            }

            string outDir = GetOutputDirectory(pacFilePath);
            Directory.CreateDirectory(outDir);

            var fileList = EnumerateFiles(remainingData, fileCount, stringSize, entrySize);
            ExtractFilesFromMemory(pacContents, dataStart, fileList, outDir, extractedFiles);

            return extractedFiles;
        }

        private (int dataStart, int stringSize, int fileCount, int entrySize, byte[] remainingData) ParseHeader(byte[] pacContents)
        {
            if (!StartsWith(pacContents, PAC_PREFIX))
            {
                throw new Exception("不是有效的FPAC文件!");
            }

            int offset = PAC_PREFIX.Length;

            int dataStart = ReadInt32(pacContents, ref offset);
            offset += 4;
            int fileCount = ReadInt32(pacContents, ref offset);

            offset += 4;
            int stringSize = ReadInt32(pacContents, ref offset);
            offset += 8;

            byte[] remainingData = new byte[pacContents.Length - offset];
            Array.Copy(pacContents, offset, remainingData, 0, remainingData.Length);

            int entrySize = 0;
            if (fileCount > 0)
            {
                float calculatedEntrySize = (dataStart - PAC_HEADER_SIZE) / (float)fileCount;
                if (!calculatedEntrySize.Equals((int)calculatedEntrySize))
                {
                    throw new Exception($"无效的文件条目大小{calculatedEntrySize}!");
                }
                entrySize = (int)calculatedEntrySize;
            }

            return (dataStart, stringSize, fileCount, entrySize, remainingData);
        }

        private List<PacFileEntry> EnumerateFiles(byte[] remainingData, int fileCount, int stringSize, int entrySize)
        {
            var fileList = new List<PacFileEntry>();

            int intFieldCount = (entrySize - stringSize) / INT_SIZE;
            if ((entrySize - stringSize) % INT_SIZE != 0)
            {
                throw new Exception("文件条目大小与字符串大小不匹配!");
            }

            Encoding latin1 = Encoding.GetEncoding(28591);

            for (int i = 0; i < fileCount; i++)
            {
                int entryOffset = i * entrySize;
                if (entryOffset + entrySize > remainingData.Length)
                {
                    throw new Exception("文件条目数据超出范围");
                }

                byte[] fileNameBytes = new byte[stringSize];
                Array.Copy(remainingData, entryOffset, fileNameBytes, 0, stringSize);
                string fileName = latin1.GetString(fileNameBytes).TrimEnd('\0');

                int dataOffset = entryOffset + stringSize;
                int[] intFields = new int[intFieldCount];
                for (int j = 0; j < intFieldCount; j++)
                {
                    intFields[j] = ReadInt32(remainingData, ref dataOffset);
                }

                int fileId = intFields.Length > 0 ? intFields[0] : 0;
                int fileOffset = intFields.Length > 1 ? intFields[1] : 0;
                int fileSize = intFields.Length > 2 ? intFields[2] : 0;

                fileList.Add(new PacFileEntry(fileName, fileId, fileOffset, fileSize));
            }

            return fileList;
        }

        private void ExtractFilesFromMemory(byte[] pacContents, int dataStart, List<PacFileEntry> fileList, string outDir, List<string> extractedFiles)
        {
            foreach (var fileEntry in fileList)
            {
                try
                {
                    int absoluteOffset = dataStart + fileEntry.FileOffset;
                    if (absoluteOffset + fileEntry.FileSize > pacContents.Length)
                    {
                        ExtractionError?.Invoke(this, $"文件{fileEntry.FileName} 数据超出范围，跳过");
                        continue;
                    }

                    byte[] fileData = new byte[fileEntry.FileSize];
                    Array.Copy(pacContents, absoluteOffset, fileData, 0, fileEntry.FileSize);

                    string fullPath = Path.Combine(outDir, fileEntry.FileName);
                    string? directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllBytes(fullPath, fileData);
                    extractedFiles.Add(fullPath);
                    ExtractionProgress?.Invoke(this, $"已提取:{fileEntry.FileName}");
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"提取文件{fileEntry.FileName} 失败:{ex.Message}");
                }
            }
        }

        private string GetOutputDirectory(string pacFilePath)
        {
            string? parentDir = Path.GetDirectoryName(pacFilePath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(pacFilePath);

            fileNameWithoutExt = fileNameWithoutExt.Replace("_decompressed", "");

            if (string.IsNullOrEmpty(parentDir))
            {
                parentDir = Directory.GetCurrentDirectory();
            }

            return Path.Combine(parentDir, fileNameWithoutExt);
        }

        private bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i]) return false;
            }
            return true;
        }

        private int ReadInt32(byte[] data, ref int offset)
        {
            if (offset + 4 > data.Length)
                throw new Exception("读取超出数据范围");

            int value = BitConverter.ToInt32(data, offset);
            offset += 4;
            return value;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private class PacFileEntry
        {
            public string FileName { get; }
            public int FileId { get; }
            public int FileOffset { get; }
            public int FileSize { get; }

            public PacFileEntry(string fileName, int fileId, int fileOffset, int fileSize)
            {
                FileName = fileName;
                FileId = fileId;
                FileOffset = fileOffset;
                FileSize = fileSize;
            }
        }
    }
}