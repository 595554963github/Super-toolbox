using System.Text.RegularExpressions;
using AFSLib;

namespace super_toolbox
{
    public class AfsExtractor : BaseExtractor
    {
        private readonly object _lockObject = new object();
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] AFS_MAGIC_NUMBER = { 0x41, 0x46, 0x53 };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath} 不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath} 不是有效的目录");
                return;
            }

            string extractedRootDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedRootDir);

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedRootDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = allFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始扫描{allFiles.Count}个文件，查找AFS容器");

            try
            {
                await Task.Run(() =>
                {
                    int afsFilesCount = 0;
                    int totalExtractedEntries = 0;

                    foreach (var filePath in allFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (!IsAfsFile(filePath))
                            {
                                continue;
                            }

                            afsFilesCount++;
                            string afsFileName = Path.GetFileName(filePath);
                            string afsExtractDir = Path.Combine(extractedRootDir, Path.GetFileNameWithoutExtension(filePath));
                            Directory.CreateDirectory(afsExtractDir);

                            ExtractionProgress?.Invoke(this, $"发现AFS容器({afsFilesCount}): {afsFileName}");

                            using (AFS afs = new AFS(filePath))
                            {
                                int entryCount = 1;
                                int totalEntries = (int)afs.EntryCount;
                                int processedEntries = 0;

                                ExtractionProgress?.Invoke(this, $"AFS内包含{totalEntries}个条目");

                                for (int e = 0; e < totalEntries; e++)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();

                                    if (afs.Entries[e] is NullEntry)
                                    {
                                        continue;
                                    }

                                    if (afs.Entries[e] is DataEntry dataEntry)
                                    {
                                        string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                                        string fileExtension = GetFileExtensionFromName(dataEntry.SanitizedName);
                                        string outputFileName = $"{baseFileName}_{entryCount}{fileExtension}";
                                        string outputFilePath = Path.Combine(afsExtractDir, outputFileName);
                                        outputFilePath = GetUniqueFilePath(outputFilePath);

                                        try
                                        {
                                            lock (_lockObject)
                                            {
                                                afs.ExtractEntryToFile(dataEntry, outputFilePath);
                                            }

                                            processedEntries++;
                                            totalExtractedEntries++;
                                            entryCount++;

                                            OnFileExtracted(outputFilePath);
                                            ExtractionProgress?.Invoke(this, $"已提取:{outputFileName} ({processedEntries}/{totalEntries})");
                                        }
                                        catch (Exception ex)
                                        {
                                            ExtractionError?.Invoke(this, $"提取条目失败:{outputFileName} - {ex.Message}");
                                        }
                                    }
                                }

                                ExtractionProgress?.Invoke(this, $"完成处理:{afsFileName} -> {processedEntries}/{totalEntries}个条目");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理{Path.GetFileName(filePath)}时出错:{ex.Message}");
                            OnExtractionFailed($"处理{Path.GetFileName(filePath)}时出错:{ex.Message}");
                        }
                    }

                    if (afsFilesCount > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"处理完成，共发现{afsFilesCount}个AFS容器，提取出{totalExtractedEntries}个文件");
                    }
                    else
                    {
                        ExtractionProgress?.Invoke(this, "处理完成，未发现AFS容器");
                    }
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }
        private bool IsAfsFile(string filePath)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 3)
                        return false;

                    byte[] header = new byte[3];
                    int bytesRead = fs.Read(header, 0, 3);

                    return bytesRead == 3 &&
                           header[0] == AFS_MAGIC_NUMBER[0] &&
                           header[1] == AFS_MAGIC_NUMBER[1] &&
                           header[2] == AFS_MAGIC_NUMBER[2];
                }
            }
            catch
            {
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private string GetFileExtensionFromName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return ".bin";

            string cleanName = RemoveParentheses(fileName);
            if (Path.HasExtension(cleanName))
            {
                string extension = Path.GetExtension(cleanName).ToLowerInvariant();
                return extension switch
                {
                    ".wav" or ".wave" => ".wav",
                    ".adx" => ".adx",
                    ".ahx" => ".ahx",
                    ".hca" => ".hca",
                    ".ogg" => ".ogg",
                    ".mp3" => ".mp3",
                    ".jpg" or ".jpeg" => ".jpg",
                    ".png" => ".png",
                    ".bmp" => ".bmp",
                    ".dds" => ".dds",
                    ".tga" => ".tga",
                    ".gif" => ".gif",
                    ".txt" => ".txt",
                    ".xml" => ".xml",
                    ".json" => ".json",
                    ".bin" or ".dat" => ".bin",
                    _ => ".bin"
                };
            }
            return ".bin";
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

        private string RemoveParentheses(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            string noBrackets = Regex.Replace(fileName, @"\([^)]*\)", "");

            if (string.IsNullOrWhiteSpace(noBrackets))
            {
                noBrackets = fileName.Replace("(", "").Replace(")", "");
            }

            return noBrackets.Trim();
        }
    }
}
