using System.Text;

namespace super_toolbox
{
    public class UKArc_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const int ENTRY_SIZE = 76;
        private const int HEADER_SIZE = 16;
        private const int FILENAME_LENGTH = 64;
        private static readonly byte[] UKARC_MAGIC = Encoding.ASCII.GetBytes("UKArc");

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"目录不存在:{directoryPath}");
                OnExtractionFailed($"目录不存在:{directoryPath}");
                return;
            }

            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                .ToList();

            TotalFilesToExtract = files.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{files.Count}个文件");

            int totalExtractedFiles = 0;

            foreach (var filePath in files)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ExtractionProgress?.Invoke(this, $"正在处理{Path.GetFileName(filePath)}");

                    int extractedCount = await ExtractFromFileAsync(filePath, directoryPath, cancellationToken);
                    totalExtractedFiles += extractedCount;

                    if (extractedCount > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(filePath)}中提取出{extractedCount}个文件");
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "操作已取消");
                    OnExtractionFailed("操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                }
            }

            if (totalExtractedFiles > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{totalExtractedFiles}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到UKArc格式文件");
            }

            OnExtractionCompleted();
        }

        private async Task<int> ExtractFromFileAsync(string filePath, string extractedDir, CancellationToken cancellationToken)
        {
            byte[] data = await File.ReadAllBytesAsync(filePath, cancellationToken);

            if (!IsUKArcFile(data))
            {
                return 0;
            }

            var entries = ParseIndex(data);
            if (entries.Count == 0)
            {
                return 0;
            }

            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string outputDir = Path.Combine(extractedDir, baseName);
            Directory.CreateDirectory(outputDir);

            int extractedCount = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    int start = entry.FileOffset;
                    int end = start + entry.Size;

                    if (end <= data.Length)
                    {
                        byte[] fileData = new byte[entry.Size];
                        Array.Copy(data, start, fileData, 0, entry.Size);

                        string outputPath = Path.Combine(outputDir, entry.Filename);
                        string outputDir2 = Path.GetDirectoryName(outputPath) ?? outputDir;
                        Directory.CreateDirectory(outputDir2);

                        outputPath = GetUniqueFilePath(outputPath);

                        await File.WriteAllBytesAsync(outputPath, fileData, cancellationToken);

                        OnFileExtracted(outputPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{entry.Filename}");
                        extractedCount++;
                    }
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"提取文件{entry.Filename}时出错:{ex.Message}");
                }
            }

            return extractedCount;
        }

        private bool IsUKArcFile(byte[] data)
        {
            if (data.Length < UKARC_MAGIC.Length)
                return false;

            for (int i = 0; i < UKARC_MAGIC.Length; i++)
            {
                if (data[i] != UKARC_MAGIC[i])
                    return false;
            }

            return true;
        }

        private List<IndexEntry> ParseIndex(byte[] data)
        {
            var entries = new List<IndexEntry>();
            int offset = HEADER_SIZE;

            if (data.Length < offset + ENTRY_SIZE)
                return entries;

            int firstFileRawOffset = BitConverter.ToInt32(data, offset + 72);
            int indexEnd = firstFileRawOffset + HEADER_SIZE;

            while (offset + ENTRY_SIZE <= data.Length && offset < indexEnd)
            {
                byte[] filenameBytes = new byte[FILENAME_LENGTH];
                Array.Copy(data, offset, filenameBytes, 0, FILENAME_LENGTH);

                string filename = Encoding.ASCII.GetString(filenameBytes)
                    .TrimEnd('\0');

                if (string.IsNullOrEmpty(filename))
                    break;

                int size = BitConverter.ToInt32(data, offset + 64);
                int unknown = BitConverter.ToInt32(data, offset + 68);
                int rawOffset = BitConverter.ToInt32(data, offset + 72);
                int fileOffset = rawOffset + HEADER_SIZE;

                entries.Add(new IndexEntry
                {
                    Filename = filename,
                    Size = size,
                    RawOffset = rawOffset,
                    FileOffset = fileOffset,
                    Unknown = unknown
                });

                offset += ENTRY_SIZE;
            }

            return entries;
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int count = 1;

            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{count}{extension}");
                count++;
            } while (File.Exists(newPath));

            return newPath;
        }

        private class IndexEntry
        {
            public string Filename { get; set; } = string.Empty;
            public int Size { get; set; }
            public int RawOffset { get; set; }
            public int FileOffset { get; set; }
            public int Unknown { get; set; }
        }
    }
}