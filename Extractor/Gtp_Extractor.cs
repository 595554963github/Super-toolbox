using System.Text;

namespace super_toolbox
{
    public class Gtp_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            string[] gtpFiles = Directory.GetFiles(directoryPath, "*.gtp", SearchOption.TopDirectoryOnly);
            if (gtpFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, $"在目录{directoryPath}中未找到.gtp文件");
                OnExtractionFailed($"在目录{directoryPath}中未找到.gtp文件");
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到{gtpFiles.Length}个.gtp文件");

            int totalExtracted = 0;

            foreach (string gtpFilePath in gtpFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);

                try
                {
                    int extracted = await ExtractSingleGtpFile(gtpFilePath, directoryPath, cancellationToken);
                    totalExtracted += extracted;
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(gtpFilePath)}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件{Path.GetFileName(gtpFilePath)}时出错:{ex.Message}");
                }
            }

            TotalFilesToExtract = totalExtracted;

            if (totalExtracted > 0)
            {
                ExtractionProgress?.Invoke(this, $"提取完成，共提取{totalExtracted}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未提取到文件");
            }

            OnExtractionCompleted();
        }

        private async Task<int> ExtractSingleGtpFile(string gtpFilePath, string baseDirectory, CancellationToken cancellationToken)
        {
            ExtractionProgress?.Invoke(this, $"开始处理文件:{Path.GetFileName(gtpFilePath)}");

            try
            {
                byte[] gtpData = await File.ReadAllBytesAsync(gtpFilePath, cancellationToken);

                if (gtpData.Length < 0x40)
                {
                    ExtractionError?.Invoke(this, $"文件{Path.GetFileName(gtpFilePath)}太小，不是有效的GTP格式");
                    return 0;
                }

                if (Encoding.ASCII.GetString(gtpData, 0, 4) != "S16R")
                {
                    ExtractionError?.Invoke(this, $"文件 {Path.GetFileName(gtpFilePath)} 不是有效的GTP格式");
                    return 0;
                }

                if (Encoding.ASCII.GetString(gtpData, 0x20, 7) != "STRMFAT")
                {
                    ExtractionError?.Invoke(this, $"文件{Path.GetFileName(gtpFilePath)}缺少STRMFAT签名");
                    return 0;
                }

                uint dataSectionOffset = BitConverter.ToUInt32(gtpData, 0x3C);

                if (dataSectionOffset + 8 > gtpData.Length ||
                    Encoding.ASCII.GetString(gtpData, (int)dataSectionOffset, 8) != "STRMFILE")
                {
                    ExtractionError?.Invoke(this, $"文件{Path.GetFileName(gtpFilePath)} 缺少STRMFILE签名");
                    return 0;
                }

                string outputDir = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(gtpFilePath));
                Directory.CreateDirectory(outputDir);

                ExtractionProgress?.Invoke(this, $"输出目录:{outputDir}");

                List<string> extractedFiles = new List<string>();
                int extractedCount = 0;
                int currentPos = 0x80;
                int fileSignatureCount = 0;
                uint currentFileSize = 0;
                int currentFileNamePos = 0;

                while (currentPos < dataSectionOffset && currentPos < gtpData.Length)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    if (gtpData[currentPos] == 0)
                    {
                        currentPos++;
                        continue;
                    }

                    int pathEnd = FindNullTerminator(gtpData, currentPos);
                    if (pathEnd == -1 || pathEnd >= dataSectionOffset)
                    {
                        break;
                    }

                    byte[] fullPathBytes = new byte[pathEnd - currentPos];
                    Array.Copy(gtpData, currentPos, fullPathBytes, 0, fullPathBytes.Length);

                    int markerStart = pathEnd + 1;
                    if (markerStart + 3 >= dataSectionOffset)
                    {
                        break;
                    }

                    string marker = Encoding.ASCII.GetString(gtpData, markerStart, 4);

                    if (marker == "DIR!")
                    {
                        string fullPath = Encoding.UTF8.GetString(fullPathBytes).TrimStart('/');
                        string dirPath = Path.Combine(outputDir, fullPath);
                        Directory.CreateDirectory(dirPath);
                        ExtractionProgress?.Invoke(this, $"创建目录:{fullPath}");
                        currentPos = markerStart + 4;
                    }
                    else if (marker == "FILE")
                    {
                        fileSignatureCount++;

                        if (fileSignatureCount == 1)
                        {
                            int sizeOffset = markerStart + 12;
                            if (sizeOffset + 3 >= dataSectionOffset)
                            {
                                break;
                            }

                            currentFileSize = BitConverter.ToUInt32(gtpData, sizeOffset);
                            currentFileNamePos = sizeOffset + 16;
                            currentPos = currentFileNamePos;
                        }
                        else
                        {
                            int endOffset = markerStart + 8;
                            if (endOffset + 3 >= dataSectionOffset)
                            {
                                break;
                            }

                            uint currentFileEndOffset = BitConverter.ToUInt32(gtpData, endOffset);
                            uint currentFileStartOffset = currentFileEndOffset - currentFileSize;

                            int fileNameEnd = FindNullTerminator(gtpData, currentFileNamePos);
                            if (fileNameEnd == -1 || fileNameEnd >= dataSectionOffset)
                            {
                                break;
                            }

                            string filePath = Encoding.UTF8.GetString(gtpData, currentFileNamePos, fileNameEnd - currentFileNamePos)
                                .TrimStart('/');

                            string fullOutputPath = Path.Combine(outputDir, filePath);
                            string dirPath = Path.GetDirectoryName(fullOutputPath) ?? "";

                            if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                            {
                                Directory.CreateDirectory(dirPath);
                            }

                            if (currentFileStartOffset >= dataSectionOffset && currentFileEndOffset <= gtpData.Length)
                            {
                                try
                                {
                                    byte[] fileData = new byte[currentFileSize];
                                    Array.Copy(gtpData, (int)currentFileStartOffset, fileData, 0, (int)currentFileSize);

                                    await File.WriteAllBytesAsync(fullOutputPath, fileData, cancellationToken);

                                    extractedFiles.Add(fullOutputPath);
                                    OnFileExtracted(fullOutputPath);
                                    extractedCount++;

                                    ExtractionProgress?.Invoke(this,
                                        $"提取:{filePath}(大小:{currentFileSize}, 偏移:0x{currentFileStartOffset:X}-0x{currentFileEndOffset:X})");
                                }
                                catch (Exception ex)
                                {
                                    ExtractionError?.Invoke(this, $"失败:{filePath} - {ex.Message}");
                                    OnExtractionFailed($"失败:{filePath} - {ex.Message}");
                                }
                            }

                            int nextSizeOffset = endOffset + 4;
                            if (nextSizeOffset + 3 >= dataSectionOffset)
                            {
                                break;
                            }

                            currentFileSize = BitConverter.ToUInt32(gtpData, nextSizeOffset);
                            currentFileNamePos = nextSizeOffset + 20;
                            currentPos = currentFileNamePos;
                        }
                    }
                    else
                    {
                        currentPos++;
                    }
                }

                ExtractionProgress?.Invoke(this,
                    $"文件 {Path.GetFileName(gtpFilePath)} 处理完成，提取{extractedCount}个文件");

                return extractedCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理文件{gtpFilePath}时出错:{ex.Message}");
                OnExtractionFailed($"处理文件{gtpFilePath}时出错:{ex.Message}");
                return 0;
            }
        }

        private int FindNullTerminator(byte[] data, int startIndex)
        {
            for (int i = startIndex; i < data.Length; i++)
            {
                if (data[i] == 0)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}