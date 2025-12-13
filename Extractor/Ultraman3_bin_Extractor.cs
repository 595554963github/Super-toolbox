using System.Text;

namespace super_toolbox
{
    public class Ultraman3_bin_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const string ELF_NAME = "SLPS_254.41";

        public Ultraman3_bin_Extractor() { }

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

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                string elfPath = Path.Combine(directoryPath, ELF_NAME);
                if (!File.Exists(elfPath))
                {
                    string errorMsg = $"找不到目标ELF文件:{elfPath}";
                    ExtractionError?.Invoke(this, errorMsg);
                    OnExtractionFailed(errorMsg);
                    return;
                }

                byte[] elfData = await File.ReadAllBytesAsync(elfPath, cancellationToken);

                int fileEntriesStart = 0x214fa0;
                if (fileEntriesStart + 16 > elfData.Length)
                {
                    string errorMsg = "文件格式错误";
                    ExtractionError?.Invoke(this, errorMsg);
                    OnExtractionFailed(errorMsg);
                    return;
                }

                string[] binFiles = { "FILE1.BIN", "FILE2.BIN", "FILE3.BIN", "FILE4.BIN" };
                int currentBinIndex = 0;

                byte[]? currentBinData = null;
                string currentBinName = "";
                string currentOutputDir = "";
                uint currentOffset = 0;
                int fileIndex = 0;
                int totalFilesExtracted = 0;

                List<string> extractedFiles = new List<string>();

                int readPos = fileEntriesStart;

                while (readPos + 16 <= elfData.Length && currentBinIndex < binFiles.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (currentBinData == null)
                    {
                        currentBinName = binFiles[currentBinIndex];
                        string binFilePath = Path.Combine(directoryPath, currentBinName);

                        if (!File.Exists(binFilePath))
                        {
                            ExtractionError?.Invoke(this, $"找不到BIN文件:{binFilePath}");
                            break;
                        }

                        currentBinData = await File.ReadAllBytesAsync(binFilePath, cancellationToken);
                        currentOutputDir = Path.Combine(directoryPath, Path.GetFileNameWithoutExtension(currentBinName));
                        Directory.CreateDirectory(currentOutputDir);
                        currentOffset = 0;

                        ExtractionProgress?.Invoke(this, $"开始处理{currentBinName}...");
                    }

                    uint blockSize = BitConverter.ToUInt32(elfData, readPos);
                    readPos += 4;

                    readPos += 4;

                    uint endOffset = BitConverter.ToUInt32(elfData, readPos);
                    readPos += 4;

                    readPos += 4;

                    if (endOffset == 0 || blockSize == 0)
                    {
                        currentBinIndex++;
                        currentBinData = null;
                        continue;
                    }

                    if (currentOffset >= currentBinData.Length)
                    {
                        ExtractionError?.Invoke(this, $"文件{fileIndex}起始偏移超出{currentBinName}范围");
                        currentBinIndex++;
                        currentBinData = null;
                        continue;
                    }

                    if (blockSize > currentBinData.Length - currentOffset)
                    {
                        ExtractionError?.Invoke(this, $"文件{fileIndex}块大小超出{currentBinName}范围");
                        currentBinIndex++;
                        currentBinData = null;
                        continue;
                    }

                    byte[] fileData = new byte[blockSize];
                    Array.Copy(currentBinData, currentOffset, fileData, 0, blockSize);

                    string extension = GetExtensionFromHeader(fileData);
                    string outputFileName = $"{fileIndex}.{extension}";
                    string outputPath = Path.Combine(currentOutputDir, outputFileName);

                    int duplicate = 1;
                    while (File.Exists(outputPath))
                    {
                        outputPath = Path.Combine(currentOutputDir, $"{fileIndex}_dup{duplicate}.{extension}");
                        duplicate++;
                    }

                    await File.WriteAllBytesAsync(outputPath, fileData, cancellationToken);

                    extractedFiles.Add(outputPath);
                    OnFileExtracted(outputPath);

                    ExtractionProgress?.Invoke(this, $"提取文件{fileIndex}: 来自{currentBinName}, 偏移=0x{currentOffset:X8}, 大小={blockSize}字节, 后缀={extension}");

                    currentOffset = endOffset;
                    fileIndex++;
                    totalFilesExtracted++;

                    if (totalFilesExtracted % 100 == 0)
                    {
                        ExtractionProgress?.Invoke(this, $"已提取{totalFilesExtracted}个文件...");
                    }
                }

                TotalFilesToExtract = totalFilesExtracted;
                ExtractionProgress?.Invoke(this, $"完成!成功提取{totalFilesExtracted}个文件");
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
                string errorMsg = $"提取失败:{ex.Message}";
                ExtractionError?.Invoke(this, errorMsg);
                OnExtractionFailed(errorMsg);
                throw;
            }
        }

        private string GetExtensionFromHeader(byte[] data)
        {
            if (data.Length < 4) return "bin";

            StringBuilder extension = new StringBuilder();

            for (int i = 0; i < 4; i++)
            {
                byte b = data[i];

                if (b >= 0x41 && b <= 0x5A)
                {
                    extension.Append((char)(b + 0x20));
                }
                else if (b >= 0x61 && b <= 0x7A)
                {
                    extension.Append((char)b);
                }
                else
                {
                    break;
                }
            }

            string ext = extension.ToString();
            if (ext.Length >= 2)
            {
                foreach (char c in ext)
                {
                    if (c < 'a' || c > 'z')
                    {
                        return "bin";
                    }
                }
                return ext;
            }

            return "bin";
        }
    }
}