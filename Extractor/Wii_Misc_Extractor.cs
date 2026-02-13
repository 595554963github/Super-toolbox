namespace super_toolbox
{
    public class Wii_Misc_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] BresSignature = new byte[] { 0x62, 0x72, 0x65, 0x73 };
        private static readonly byte[] TplSignature = new byte[] { 0x00, 0x20, 0xAF, 0x30 };
        private static readonly byte[] RlanSignature = new byte[] { 0x52, 0x4C, 0x41, 0x4E };
        private static readonly byte[] RlytSignature = new byte[] { 0x52, 0x4C, 0x59, 0x54 };
        private const int TplHeaderSize = 64;
        private class FormatInfo
        {
            public byte[]? Signature { get; set; }
            public string? Extension { get; set; }
            public int SizeOffset { get; set; }
            public int? HeaderSize { get; set; }
        }
        private readonly FormatInfo[] Formats = new[]
        {
            new FormatInfo { Signature = BresSignature, Extension = "brres", SizeOffset = 0x08, HeaderSize = null },
            new FormatInfo { Signature = RlanSignature, Extension = "brlan", SizeOffset = 0x08, HeaderSize = null },
            new FormatInfo { Signature = RlytSignature, Extension = "brlyt", SizeOffset = 0x08, HeaderSize = null },
            new FormatInfo { Signature = TplSignature, Extension = "tpl", SizeOffset = 0x14, HeaderSize = 64 }
        };
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
            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);
            TotalFilesToExtract = files.Count();
            int processedFiles = 0;
            foreach (var file in files)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(file)}");
                try
                {
                    string outputDir = Path.Combine(Path.GetDirectoryName(file) ?? directoryPath,
                                                   $"{Path.GetFileNameWithoutExtension(file)}");
                    Directory.CreateDirectory(outputDir);
                    byte[] content = await File.ReadAllBytesAsync(file, cancellationToken);
                    string fileNamePrefix = Path.GetFileNameWithoutExtension(file);
                    foreach (var format in Formats)
                    {
                        await ExtractFilesByFormat(content, fileNamePrefix, outputDir, extractedFiles, format, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{file}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{file}时出错:{e.Message}");
                }

                processedFiles++;
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到有效文件");
            }
            OnExtractionCompleted();
        }
        private async Task ExtractFilesByFormat(byte[] content, string fileNamePrefix, string outputDir,
                                              List<string> extractedFiles, FormatInfo format,
                                              CancellationToken cancellationToken)
        {
            if (format == null)
                throw new ArgumentNullException(nameof(format));
            if (format.Signature == null)
                throw new ArgumentException("格式签名不能为空", nameof(format));
            int index = 0;
            int fileIndex = 1;
            while (index <= content.Length - format.Signature.Length)
            {
                int startIndex = IndexOf(content, format.Signature, index);
                if (startIndex == -1) break;
                int sizeOffsetPos = startIndex + format.SizeOffset;
                if (sizeOffsetPos + 4 > content.Length)
                {
                    index = startIndex + 1;
                    continue;
                }
                int fileSize = ReadBigEndianInt32(content, sizeOffsetPos);
                int totalSize = fileSize;
                if (format.HeaderSize.HasValue)
                {
                    totalSize = format.HeaderSize.Value + fileSize;
                }
                if (totalSize <= 0 || startIndex + totalSize > content.Length || totalSize > 100 * 1024 * 1024)
                {
                    index = startIndex + 1;
                    continue;
                }
                byte[] fileData = new byte[totalSize];
                Array.Copy(content, startIndex, fileData, 0, totalSize);
                string fileName = $"{fileNamePrefix}_{fileIndex}.{format.Extension}";
                string outputPath = Path.Combine(outputDir, fileName);
                outputPath = GetUniqueFilePath(outputPath);
                await File.WriteAllBytesAsync(outputPath, fileData, cancellationToken);
                extractedFiles.Add(outputPath);
                OnFileExtracted(outputPath);
                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");

                fileIndex++;
                index = startIndex + totalSize;
            }
        }
        private int ReadBigEndianInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }
        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            if (data == null || pattern == null || startIndex < 0 || startIndex > data.Length - pattern.Length)
                return -1;

            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
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
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }
    }
}