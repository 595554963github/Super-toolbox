using System.IO.Compression;
using System.Text;

namespace super_toolbox
{
    public class GPDA_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const string GPDA_SIGNATURE = "GPDA";
        private int _totalExtractedFiles = 0;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .Where(IsGpdaFile)
                .ToList();

            if (filePaths.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到GPDA文件");
                OnExtractionFailed("未找到GPDA文件");
                return;
            }

            TotalFilesToExtract = filePaths.Count;
            _totalExtractedFiles = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ExtractionProgress?.Invoke(this, $"正在处理源文件:{Path.GetFileName(filePath)}");

                    try
                    {
                        var filesFromThisArchive = await ExtractGpdaFile(filePath, extractedDir, cancellationToken);
                        extractedFiles.AddRange(filesFromThisArchive);
                        ExtractionProgress?.Invoke(this, $"完成处理源文件:{Path.GetFileName(filePath)} -> 提取出了{filesFromThisArchive.Count}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (IOException e)
                    {
                        ExtractionError?.Invoke(this, $"读取文件{filePath}时出错:{e.Message}");
                        OnExtractionFailed($"读取文件{filePath}时出错:{e.Message}");
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                    }
                }

                UpdateFinalFileCount(extractedFiles.Count);

                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成，共从{filePaths.Count}个源文件中提取出{extractedFiles.Count}个文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到可提取的文件");
                }
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task<List<string>> ExtractGpdaFile(string filePath, string outputDir, CancellationToken cancellationToken)
        {
            var extractedFiles = new List<string>();

            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string gpdaOutputDir = Path.Combine(outputDir, baseFileName);

            if (Directory.Exists(gpdaOutputDir))
            {
                Directory.Delete(gpdaOutputDir, true);
            }
            Directory.CreateDirectory(gpdaOutputDir);

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            await ExtractGpdaArchive(reader, 0, gpdaOutputDir, "", extractedFiles, cancellationToken, isRoot: true);

            return extractedFiles;
        }

        private async Task ExtractGpdaArchive(BinaryReader reader, long baseOffset, string outputDir, string currentPath, List<string> extractedFiles, CancellationToken cancellationToken, bool isRoot = false)
        {
            long backupPosition = reader.BaseStream.Position;

            try
            {
                reader.BaseStream.Seek(baseOffset, SeekOrigin.Begin);

                byte[] signatureBytes = reader.ReadBytes(4);
                string signature = Encoding.ASCII.GetString(signatureBytes);

                if (signature != GPDA_SIGNATURE)
                {
                    return;
                }

                uint archiveSize = reader.ReadUInt32();
                uint zero = reader.ReadUInt32();
                uint fileCount = reader.ReadUInt32();

                ExtractionProgress?.Invoke(this, $"发现{fileCount}个文件在归档中(偏移量:{baseOffset})");

                var fileEntries = new List<GpdaFileEntry>();
                for (int i = 0; i < fileCount; i++)
                {
                    uint fileOffset = reader.ReadUInt32();
                    uint isCompressed = reader.ReadUInt32();
                    uint fileSize = reader.ReadUInt32();
                    uint nameOffset = reader.ReadUInt32();

                    fileEntries.Add(new GpdaFileEntry
                    {
                        Offset = fileOffset,
                        IsCompressed = isCompressed,
                        Size = fileSize,
                        NameOffset = nameOffset
                    });
                }

                for (int i = 0; i < fileEntries.Count; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    var entry = fileEntries[i];
                    long currentPosition = reader.BaseStream.Position;

                    reader.BaseStream.Seek(baseOffset + entry.NameOffset, SeekOrigin.Begin);
                    uint nameSize = reader.ReadUInt32();
                    byte[] nameBytes = reader.ReadBytes((int)nameSize);
                    string fileName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                    reader.BaseStream.Seek(currentPosition, SeekOrigin.Begin);

                    long actualFileOffset = baseOffset + entry.Offset;

                    reader.BaseStream.Seek(actualFileOffset, SeekOrigin.Begin);
                    byte[] fileSignatureBytes = reader.ReadBytes(4);
                    string fileSignature = Encoding.ASCII.GetString(fileSignatureBytes);
                    reader.BaseStream.Seek(currentPosition, SeekOrigin.Begin);

                    string extension = Path.GetExtension(fileName).ToLower();
                    if (extension == ".gz" || extension == ".gz ")
                    {
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                        entry.IsCompressed = 1;
                    }

                    if (fileSignature == GPDA_SIGNATURE)
                    {
                        string nestedFileName = Path.GetFileNameWithoutExtension(fileName);
                        string nestedPath = Path.Combine(currentPath, nestedFileName);
                        string nestedOutputDir = Path.Combine(outputDir, nestedPath);

                        Directory.CreateDirectory(nestedOutputDir);

                        await ExtractGpdaArchive(reader, actualFileOffset, outputDir, nestedPath, extractedFiles, cancellationToken);
                    }
                    else
                    {
                        if (entry.IsCompressed == 1)
                        {
                            byte[] compressedData = ReadFileData(reader, actualFileOffset, entry.Size);

                            try
                            {
                                byte[] decompressedData = await DecompressGzip(compressedData);
                                if (decompressedData.Length >= 4)
                                {
                                    string decompressedSignature = Encoding.ASCII.GetString(decompressedData, 0, 4);
                                    if (decompressedSignature == GPDA_SIGNATURE)
                                    {
                                        string nestedFileName = Path.GetFileNameWithoutExtension(fileName);
                                        string nestedPath = Path.Combine(currentPath, nestedFileName);
                                        string nestedOutputDir = Path.Combine(outputDir, nestedPath);

                                        Directory.CreateDirectory(nestedOutputDir);

                                        using var memoryStream = new MemoryStream(decompressedData);
                                        using var memoryReader = new BinaryReader(memoryStream);
                                        await ExtractGpdaArchiveFromMemory(memoryReader, 0, outputDir, nestedPath, extractedFiles, cancellationToken);
                                        continue;
                                    }
                                }
                            }
                            catch
                            {
                            }

                            await ExtractCompressedFile(reader, actualFileOffset, entry.Size, outputDir, currentPath, fileName, extractedFiles);
                        }
                        else
                        {
                            await ExtractUncompressedFile(reader, actualFileOffset, entry.Size, outputDir, currentPath, fileName, extractedFiles);
                        }
                    }
                }
            }
            finally
            {
                reader.BaseStream.Seek(backupPosition, SeekOrigin.Begin);
            }
        }

        private async Task ExtractGpdaArchiveFromMemory(BinaryReader reader, long baseOffset, string outputDir, string currentPath, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            long backupPosition = reader.BaseStream.Position;

            try
            {
                reader.BaseStream.Seek(baseOffset, SeekOrigin.Begin);

                byte[] signatureBytes = reader.ReadBytes(4);
                string signature = Encoding.ASCII.GetString(signatureBytes);

                if (signature != GPDA_SIGNATURE)
                {
                    return;
                }

                uint archiveSize = reader.ReadUInt32();
                uint zero = reader.ReadUInt32();
                uint fileCount = reader.ReadUInt32();

                for (int i = 0; i < fileCount; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    uint fileOffset = reader.ReadUInt32();
                    uint isCompressed = reader.ReadUInt32();
                    uint fileSize = reader.ReadUInt32();
                    uint nameOffset = reader.ReadUInt32();

                    long currentPosition = reader.BaseStream.Position;

                    reader.BaseStream.Seek(baseOffset + nameOffset, SeekOrigin.Begin);
                    uint nameSize = reader.ReadUInt32();
                    byte[] nameBytes = reader.ReadBytes((int)nameSize);
                    string fileName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                    reader.BaseStream.Seek(currentPosition, SeekOrigin.Begin);

                    long actualFileOffset = baseOffset + fileOffset;
                    string fullOutputPath = Path.Combine(outputDir, currentPath, fileName);
                    string outputDirectory = Path.GetDirectoryName(fullOutputPath) ?? outputDir;

                    Directory.CreateDirectory(outputDirectory);

                    try
                    {
                        if (isCompressed == 0)
                        {
                            reader.BaseStream.Seek(actualFileOffset, SeekOrigin.Begin);
                            byte[] fileData = reader.ReadBytes((int)fileSize);
                            await File.WriteAllBytesAsync(fullOutputPath, fileData);
                        }
                        else
                        {
                            reader.BaseStream.Seek(actualFileOffset, SeekOrigin.Begin);
                            byte[] compressedData = reader.ReadBytes((int)fileSize);
                            byte[] decompressedData = await DecompressGzip(compressedData);
                            await File.WriteAllBytesAsync(fullOutputPath, decompressedData);
                        }

                        extractedFiles.Add(fullOutputPath);
                        _totalExtractedFiles++;
                        OnFileExtracted(fullOutputPath);
                        ExtractionProgress?.Invoke(this, $"已提取(内存):{Path.Combine(currentPath, fileName)} (总提取数:{_totalExtractedFiles})");
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"提取内存文件{fileName}时出错:{ex.Message}");
                    }
                }
            }
            finally
            {
                reader.BaseStream.Seek(backupPosition, SeekOrigin.Begin);
            }
        }

        private async Task ExtractUncompressedFile(BinaryReader reader, long offset, uint size, string outputDir, string currentPath, string fileName, List<string> extractedFiles)
        {
            string fullOutputPath = Path.Combine(outputDir, currentPath, fileName);
            string outputDirectory = Path.GetDirectoryName(fullOutputPath) ?? outputDir;

            Directory.CreateDirectory(outputDirectory);

            byte[] fileData = ReadFileData(reader, offset, size);
            await File.WriteAllBytesAsync(fullOutputPath, fileData);

            extractedFiles.Add(fullOutputPath);
            _totalExtractedFiles++;
            OnFileExtracted(fullOutputPath);
            ExtractionProgress?.Invoke(this, $"已提取:{Path.Combine(currentPath, fileName)} (总提取数:{_totalExtractedFiles})");
        }

        private async Task ExtractCompressedFile(BinaryReader reader, long offset, uint size, string outputDir, string currentPath, string fileName, List<string> extractedFiles)
        {
            string fullOutputPath = Path.Combine(outputDir, currentPath, fileName);
            string outputDirectory = Path.GetDirectoryName(fullOutputPath) ?? outputDir;

            Directory.CreateDirectory(outputDirectory);

            byte[] compressedData = ReadFileData(reader, offset, size);

            try
            {
                byte[] decompressedData = await DecompressGzip(compressedData);
                await File.WriteAllBytesAsync(fullOutputPath, decompressedData);

                extractedFiles.Add(fullOutputPath);
                _totalExtractedFiles++;
                OnFileExtracted(fullOutputPath);
                ExtractionProgress?.Invoke(this, $"已提取:{Path.Combine(currentPath, fileName)} (总提取数:{_totalExtractedFiles})");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"GZip解压缩失败 {fileName}: {ex.Message}，保存原始数据");
                await File.WriteAllBytesAsync(fullOutputPath + ".compressed", compressedData);
            }
        }

        private void UpdateFinalFileCount(int totalExtractedFiles)
        {
            TotalFilesToExtract = totalExtractedFiles;
        }

        private byte[] ReadFileData(BinaryReader reader, long offset, uint size)
        {
            long backupPosition = reader.BaseStream.Position;
            try
            {
                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                return reader.ReadBytes((int)size);
            }
            finally
            {
                reader.BaseStream.Seek(backupPosition, SeekOrigin.Begin);
            }
        }

        private async Task<byte[]> DecompressGzip(byte[] compressedData)
        {
            using var compressedStream = new MemoryStream(compressedData);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();

            await gzipStream.CopyToAsync(outputStream);
            return outputStream.ToArray();
        }
        private bool IsGpdaFile(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                if (fs.Length < 16) return false;

                byte[] header = new byte[4];
                fs.Read(header, 0, 4);

                string magic = Encoding.ASCII.GetString(header);
                return magic == GPDA_SIGNATURE;
            }
            catch
            {
                return false;
            }
        }
        private class GpdaFileEntry
        {
            public uint Offset { get; set; }
            public uint IsCompressed { get; set; }
            public uint Size { get; set; }
            public uint NameOffset { get; set; }
        }
    }
}
