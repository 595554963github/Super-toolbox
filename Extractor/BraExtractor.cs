using System.Text;
using System.IO.Compression;

namespace super_toolbox
{
    public class BraExtractor : BaseExtractor
    {
        private readonly object _lockObject = new object();

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误: {directoryPath} 不是有效的目录");
                return;
            }

            var braFiles = Directory.GetFiles(directoryPath, "*.bra", SearchOption.AllDirectories)
                .Where(file => !Directory.Exists(Path.ChangeExtension(file, null)))
                .ToList();

            int totalFilesCount = 0;
            foreach (var braFile in braFiles)
            {
                try
                {
                    byte[] archiveData = File.ReadAllBytes(braFile);
                    var header = ParseHeader(archiveData);
                    totalFilesCount += (int)header.fileCount;
                }
                catch
                {
                }
            }

            TotalFilesToExtract = totalFilesCount;

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(braFiles, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    }, braFilePath =>
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string braFileName = Path.GetFileNameWithoutExtension(braFilePath);
                            string braExtractDir = Path.Combine(Path.GetDirectoryName(braFilePath)!, braFileName);
                            Directory.CreateDirectory(braExtractDir);

                            byte[] archiveData = File.ReadAllBytes(braFilePath);
                            var header = ParseHeader(archiveData);
                            var fileEntries = ParseFileEntries(archiveData, header);

                            foreach (var entry in fileEntries)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                string cleanFileName = FixFileExtension(entry.fileName);
                                string outputPath = Path.Combine(braExtractDir, cleanFileName);
                                string? outputDir = Path.GetDirectoryName(outputPath);
                                if (!string.IsNullOrEmpty(outputDir))
                                    Directory.CreateDirectory(outputDir);

                                ExtractFile(archiveData, entry, outputPath);

                                OnFileExtracted(Path.GetFileName(outputPath));
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"处理 {Path.GetFileName(braFilePath)} 时出错: {ex.Message}");
                        }
                    });
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取失败: {ex.Message}");
            }
        }

        private string FixFileExtension(string fileName)
        {
            string fileNameOnly = Path.GetFileName(fileName);
            int lastDotIndex = fileNameOnly.LastIndexOf('.');
            if (lastDotIndex < 0)
                return fileName;

            string namePart = fileNameOnly.Substring(0, lastDotIndex);
            string extPart = fileNameOnly.Substring(lastDotIndex + 1);

            string cleanExt = new string(extPart
                .Where(c => char.IsLetter(c))
                .Take(3)
                .ToArray())
                .ToLower();

            if (cleanExt.Length == 0)
                return namePart;

            if (cleanExt.Length == 3)
            {
                return namePart + "." + cleanExt;
            }

            int prevDotIndex = namePart.LastIndexOf('.');
            if (prevDotIndex > 0)
            {
                string prevExt = namePart.Substring(prevDotIndex + 1);
                if (prevExt.Length == 3 && prevExt.All(char.IsLetter))
                {
                    return fileNameOnly.Substring(0, prevDotIndex + 4);
                }
            }

            return namePart + "." + cleanExt;
        }

        private bool IsCl3File(byte[] fileHeader)
        {
            return fileHeader.Length >= 4 &&
                   fileHeader[0] == 0x43 &&
                   fileHeader[1] == 0x4C &&
                   fileHeader[2] == 0x33 &&
                   fileHeader[3] == 0x4C;
        }

        private XanaduHeader ParseHeader(byte[] archiveData)
        {
            return new XanaduHeader
            {
                fileHeader = Encoding.ASCII.GetString(SubArrayToNullTerminator(archiveData, 0)),
                compressionType = BitConverter.ToUInt32(archiveData, 4),
                fileEntryOffset = BitConverter.ToUInt32(archiveData, 8),
                fileCount = BitConverter.ToUInt32(archiveData, 12)
            };
        }

        private XanaduFileEntry[] ParseFileEntries(byte[] archiveData, XanaduHeader header)
        {
            var fileEntries = new XanaduFileEntry[header.fileCount];
            uint filePointer = header.fileEntryOffset;

            for (int i = 0; i < header.fileCount; i++)
            {
                var entry = new XanaduFileEntry
                {
                    filePackedTime = BitConverter.ToUInt32(archiveData, (int)filePointer),
                    unknown = BitConverter.ToUInt32(archiveData, (int)(filePointer + 4)),
                    compressedSize = BitConverter.ToUInt32(archiveData, (int)(filePointer + 8)),
                    uncompressedSize = BitConverter.ToUInt32(archiveData, (int)(filePointer + 12)),
                    fileNameLength = BitConverter.ToUInt16(archiveData, (int)(filePointer + 16)),
                    fileFlags = BitConverter.ToUInt16(archiveData, (int)(filePointer + 18)),
                    fileOffset = BitConverter.ToUInt32(archiveData, (int)(filePointer + 20))
                };

                filePointer += 24;
                entry.fileName = ForceValidFilePath(Encoding.ASCII.GetString(SubArray(archiveData, (int)filePointer, entry.fileNameLength)));
                filePointer += entry.fileNameLength;

                fileEntries[i] = entry;
            }

            return fileEntries;
        }

        private void ExtractFile(byte[] archiveData, XanaduFileEntry entry, string outputPath)
        {
            byte[] fileData = SubArray(archiveData, (int)(entry.fileOffset + 16), (int)(entry.compressedSize - 16));

            string finalPath = GetUniqueFileName(outputPath);
            using (var fileStream = File.Create(finalPath))
            using (var memoryStream = new MemoryStream(fileData))
            {
                if (entry.uncompressedSize == entry.compressedSize - 16)
                {
                    memoryStream.CopyTo(fileStream);
                }
                else
                {
                    using (var decompressor = new DeflateStream(memoryStream, CompressionMode.Decompress))
                    {
                        decompressor.CopyTo(fileStream);
                    }
                }
            }

            if (IsCl3File(File.ReadAllBytes(finalPath).Take(4).ToArray()))
            {
                string newPath = Path.ChangeExtension(finalPath, ".cl3");
                if (!File.Exists(newPath))
                {
                    File.Move(finalPath, newPath);
                }
            }
        }

        private string GetUniqueFileName(string originalPath)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
                throw new ArgumentException("文件路径不能为空", nameof(originalPath));

            if (!File.Exists(originalPath))
                return originalPath;

            string? directory = Path.GetDirectoryName(originalPath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);

            directory = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;

            fileNameWithoutExt = string.IsNullOrEmpty(fileNameWithoutExt) ? "file" : fileNameWithoutExt;

            int counter = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
                counter++;

                if (counter > 1000)
                    throw new IOException("无法为文件生成唯一名称，尝试次数过多");

            } while (File.Exists(newPath));

            return newPath;
        }

        private byte[] SubArray(byte[] data, int index, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        private byte[] SubArrayToNullTerminator(byte[] data, int index)
        {
            var byteList = new List<byte>();
            while (index < data.Length && data[index] != 0x00 && data[index] < 128 && !InvalidChars.Contains((char)data[index]))
            {
                byteList.Add(data[index++]);
            }
            return byteList.ToArray();
        }

        private string ForceValidFilePath(string text)
        {
            foreach (char c in InvalidChars)
            {
                if (c != '\\') text = text.Replace(c.ToString(), "");
            }
            return text ?? string.Empty;
        }

        private static readonly char[] InvalidChars = Path.GetInvalidPathChars().Union(Path.GetInvalidFileNameChars()).ToArray();

        private struct XanaduHeader
        {
            public string fileHeader;
            public uint compressionType;
            public uint fileEntryOffset;
            public uint fileCount;
        }

        private struct XanaduFileEntry
        {
            public uint filePackedTime;
            public uint unknown;
            public uint compressedSize;
            public uint uncompressedSize;
            public ushort fileNameLength;
            public ushort fileFlags;
            public uint fileOffset;
            public string fileName;
        }
    }
}