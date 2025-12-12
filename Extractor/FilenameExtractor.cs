using System.Net;
using System.Text;

namespace super_toolbox
{
    public class FilenameExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] FILENAME_HEADER = { 0x46, 0x69, 0x6C, 0x65, 0x6E, 0x61, 0x6D, 0x65 };
        private static readonly byte[] PACK_HEADER = { 0x50, 0x61, 0x63, 0x6B, 0x20, 0x20, 0x20, 0x20 };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var pckFiles = Directory.GetFiles(directoryPath, "*.pck", SearchOption.AllDirectories);

            TotalFilesToExtract = 0;

            foreach (var pckFilePath in pckFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(pckFilePath)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(pckFilePath, cancellationToken);

                    if (content.Length < 8 || !content.Take(8).SequenceEqual(FILENAME_HEADER))
                    {
                        ExtractionProgress?.Invoke(this, $"跳过无效文件:{Path.GetFileName(pckFilePath)}(不是有效的PCK文件)");
                        continue;
                    }

                    int packPos = IndexOf(content, PACK_HEADER, 0);
                    if (packPos == -1)
                    {
                        ExtractionProgress?.Invoke(this, $"跳过无效文件:{Path.GetFileName(pckFilePath)}(未找到Pack标识)");
                        continue;
                    }

                    int fileCountPos = packPos + PACK_HEADER.Length + 4;
                    if (fileCountPos + 4 > content.Length)
                    {
                        ExtractionError?.Invoke(this, $"文件 {Path.GetFileName(pckFilePath)} 格式错误");
                        continue;
                    }

                    int fileCount = BitConverter.ToInt32(content, fileCountPos);
                    if (BitConverter.IsLittleEndian)
                    {
                        fileCount = IPAddress.NetworkToHostOrder(fileCount);
                    }

                    ExtractionProgress?.Invoke(this, $"找到{fileCount}个子文件");

                    List<string> fileNames = ExtractFileNamesFromBottom(content, 8, packPos - 8, fileCount);

                    int entriesStart = fileCountPos + 4;
                    var entries = new List<(int offset, int size)>();

                    for (int i = 0; i < fileCount; i++)
                    {
                        int entryPos = entriesStart + i * 8;
                        if (entryPos + 8 > content.Length)
                        {
                            break;
                        }

                        int offset = BitConverter.ToInt32(content, entryPos);
                        int size = BitConverter.ToInt32(content, entryPos + 4);

                        if (BitConverter.IsLittleEndian)
                        {
                            offset = IPAddress.NetworkToHostOrder(offset);
                            size = IPAddress.NetworkToHostOrder(size);
                        }

                        if (offset >= 0 && size > 0)
                        {
                            entries.Add((offset, size));
                        }
                    }

                    string pckName = Path.GetFileNameWithoutExtension(pckFilePath);
                    string pckFileDir = Path.GetDirectoryName(pckFilePath) ?? string.Empty;

                    string pckOutputDir = Path.Combine(pckFileDir, pckName);
                    Directory.CreateDirectory(pckOutputDir);

                    int extractedCount = 0;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var (offset, size) = entries[i];

                        if (offset + size > content.Length)
                        {
                            ExtractionError?.Invoke(this, $"文件{pckName}条目{i + 1}数据范围无效");
                            continue;
                        }

                        byte[] fileData = new byte[size];
                        Array.Copy(content, offset, fileData, 0, size);

                        string outputFileName;
                        if (i < fileNames.Count && !string.IsNullOrEmpty(fileNames[i]))
                        {
                            outputFileName = SanitizeFileName(fileNames[i]);

                            if (string.IsNullOrEmpty(outputFileName) || !IsValidFileName(outputFileName))
                            {
                                outputFileName = $"{pckName}_{i + 1:000}";
                            }
                        }
                        else
                        {
                            outputFileName = $"{pckName}_{i + 1:000}";
                        }

                        string outputFilePath = Path.Combine(pckOutputDir, outputFileName);
                        outputFilePath = GetUniqueFilePath(outputFilePath);

                        try
                        {
                            await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);
                            extractedCount++;

                            if (!extractedFiles.Contains(outputFilePath))
                            {
                                extractedFiles.Add(outputFilePath);
                                OnFileExtracted(outputFilePath);

                                string nameInfo = (i < fileNames.Count && !string.IsNullOrEmpty(fileNames[i]))
                                    ? $"(原文件名:{fileNames[i]})"
                                    : "";
                                ExtractionProgress?.Invoke(this, $"已提取:{outputFileName} ({size}字节) {nameInfo}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"写入文件{outputFileName}时出错:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"{Path.GetFileName(pckFilePath)} 提取完成，共提取{extractedCount}个文件");
                    if (fileNames.Count > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"{Path.GetFileName(pckFilePath)} - 成功还原了{Math.Min(fileNames.Count, entries.Count)}个文件的原始文件名");
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(pckFilePath)}时出错:{ex.Message}");
                }
            }

            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到可提取的文件");
            }
            OnExtractionCompleted();
        }

        private List<string> ExtractFileNamesFromBottom(byte[] content, int startOffset, int regionSize, int expectedFileCount)
        {
            List<string> fileNames = new List<string>();

            if (regionSize <= 0 || expectedFileCount <= 0)
                return fileNames;

            int endOffset = startOffset + regionSize;

            List<string> allFileNames = new List<string>();

            int pos = startOffset;
            while (pos < endOffset)
            {
                string fileName = ReadNullTerminatedAscii(content, pos, endOffset);
                if (!string.IsNullOrEmpty(fileName) && IsValidFileName(fileName))
                {
                    allFileNames.Add(fileName);
                    pos += fileName.Length + 1;
                }
                else
                {
                    pos++;
                }
            }

            if (allFileNames.Count >= expectedFileCount)
            {
                int takeFromIndex = Math.Max(0, allFileNames.Count - expectedFileCount);
                fileNames = allFileNames.Skip(takeFromIndex).Take(expectedFileCount).ToList();
            }
            else
            {
                fileNames = allFileNames;
            }

            return fileNames;
        }

        private string ReadNullTerminatedAscii(byte[] content, int start, int maxPos)
        {
            StringBuilder sb = new StringBuilder();
            int pos = start;

            while (pos < maxPos)
            {
                byte b = content[pos];
                if (b == 0x00)
                {
                    break;
                }
                else if (b >= 0x20 && b < 0x7F)
                {
                    sb.Append((char)b);
                    pos++;
                }
                else
                {
                    return string.Empty;
                }
            }

            return sb.ToString();
        }

        private bool IsValidFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.Length < 2 || fileName.Length > 255)
                return false;
            if (fileName == "Filename" || fileName == "Pack" ||
                fileName.Contains("\\") || fileName.Contains("/") || fileName.Contains(":"))
                return false;
            foreach (char c in fileName)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
                {
                    return false;
                }
            }
            if (!fileName.Contains('.') && !fileName.Any(char.IsLetter))
            {
                return false;
            }

            return true;
        }
        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            StringBuilder sb = new StringBuilder();
            foreach (char c in fileName)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private new static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
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

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return filePath;
            }

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);
            int duplicateCount = 1;
            string newFilePath;

            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_dup{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("提取操作已取消", cancellationToken);
            }
        }
    }
}
