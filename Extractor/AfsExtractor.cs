using System.Text.RegularExpressions;
using AFSLib;

namespace super_toolbox
{
    public class AfsExtractor : BaseExtractor
    {
        private readonly object _lockObject = new object();
        public new event EventHandler<string>? ExtractionProgress;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var files = Directory.EnumerateFiles(directoryPath, "*.afs", SearchOption.AllDirectories)
               .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
               .ToList();
            TotalFilesToExtract = files.Count;
            ExtractionProgress?.Invoke(this, $"开始处理{files.Count}个AFS文件...");
            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, filePath =>
                    {
                        try
                        {
                            string afsFileName = Path.GetFileNameWithoutExtension(filePath);
                            string afsExtractedDir = Path.Combine(extractedDir, afsFileName);
                            Directory.CreateDirectory(afsExtractedDir);
                            ExtractionProgress?.Invoke(this, $"处理文件:{afsFileName}.afs");
                            using (AFS afs = new AFS(filePath))
                            {
                                int entryCount = 1; 
                                for (int e = 0; e < afs.EntryCount; e++)
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
                                        string outputFilePath = Path.Combine(afsExtractedDir, outputFileName);
                                        outputFilePath = GetUniqueFilePath(outputFilePath);
                                        lock (_lockObject)
                                        {
                                            afs.ExtractEntryToFile(dataEntry, outputFilePath);
                                        }
                                        OnFileExtracted(outputFilePath);
                                        ExtractionProgress?.Invoke(this, $"已提取: {outputFileName}");

                                        entryCount++; 
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"处理文件时出错:{ex.Message}");
                        }
                    });
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取过程中出错: {ex.Message}");
            }
            sw.Stop();
            ExtractionProgress?.Invoke(this, $"处理完成，耗时{sw.Elapsed.TotalSeconds:F2}秒");
            OnExtractionCompleted();
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