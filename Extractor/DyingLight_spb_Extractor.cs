using System.Text;

namespace super_toolbox
{
    public class DyingLight_spb_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        public DyingLight_spb_Extractor() { }

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
                var spbFiles = Directory.GetFiles(directoryPath, "*.spb", SearchOption.AllDirectories);

                if (spbFiles.Length == 0)
                {
                    string errorMsg = "目录中未找到.spb文件";
                    ExtractionError?.Invoke(this, errorMsg);
                    OnExtractionFailed(errorMsg);
                    return;
                }

                int totalFilesExtracted = 0;
                List<string> extractedFiles = new List<string>();

                foreach (var spbFilePath in spbFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[] spbData = await File.ReadAllBytesAsync(spbFilePath, cancellationToken);

                    if (spbData.Length < 0x10)
                    {
                        ExtractionError?.Invoke(this, $"文件过小,非有效spb:{spbFilePath}");
                        continue;
                    }

                    uint entryCount = BitConverter.ToUInt32(spbData, 0x08);

                    if (entryCount == 0 || entryCount > 100000)
                    {
                        ExtractionError?.Invoke(this, $"条目数量异常({entryCount}):{spbFilePath}");
                        continue;
                    }

                    uint firstEntryOffset = BitConverter.ToUInt32(spbData, 0x14);
                    uint dataAreaStart = firstEntryOffset;

                    uint indexAreaEnd = 0x10 + entryCount * 80;
                    if (indexAreaEnd > spbData.Length || dataAreaStart > spbData.Length)
                    {
                        ExtractionError?.Invoke(this, $"索引区或数据区超出文件范围:{spbFilePath}");
                        continue;
                    }

                    string spbName = Path.GetFileNameWithoutExtension(spbFilePath);
                    string outputDir = Path.Combine(directoryPath, spbName);
                    Directory.CreateDirectory(outputDir);

                    ExtractionProgress?.Invoke(this, $"处理:{Path.GetFileName(spbFilePath)}, 条目数:{entryCount}");

                    for (int i = 0; i < entryCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        uint blockBase = 0x10 + (uint)(i * 80);

                        uint fileOffset = BitConverter.ToUInt32(spbData, (int)(blockBase + 4));
                        uint fileSize = BitConverter.ToUInt32(spbData, (int)(blockBase + 8));

                        int nameStart = (int)(blockBase + 16);
                        int nameEnd = Array.IndexOf(spbData, (byte)0x00, nameStart);
                        if (nameEnd < 0) nameEnd = (int)(blockBase + 80);

                        string fileName = Encoding.ASCII.GetString(spbData, nameStart, nameEnd - nameStart);

                        if (fileOffset < dataAreaStart)
                        {
                            ExtractionError?.Invoke(this, $"条目{i}偏移0x{fileOffset:X8}位于索引区内,跳过");
                            continue;
                        }

                        if (fileOffset + fileSize > spbData.Length)
                        {
                            ExtractionError?.Invoke(this, $"条目{i}({fileName})数据超出文件范围,跳过");
                            continue;
                        }

                        byte[] fileData = new byte[fileSize];
                        Array.Copy(spbData, fileOffset, fileData, 0, fileSize);

                        string outputPath = Path.Combine(outputDir, fileName);
                        int duplicate = 1;
                        while (File.Exists(outputPath))
                        {
                            outputPath = Path.Combine(outputDir, $"{fileName}_dup{duplicate}");
                            duplicate++;
                        }

                        await File.WriteAllBytesAsync(outputPath, fileData, cancellationToken);
                        extractedFiles.Add(outputPath);
                        OnFileExtracted(outputPath);

                        ExtractionProgress?.Invoke(this, $"[{i + 1}/{entryCount}] {fileName} 偏移=0x{fileOffset:X8} 大小={fileSize}");

                        totalFilesExtracted++;

                        if (totalFilesExtracted % 100 == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"已提取{totalFilesExtracted}个文件...");
                        }
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
    }
}