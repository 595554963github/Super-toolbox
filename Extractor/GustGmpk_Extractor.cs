using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class GustGmpk_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }
            var gmpkFiles = Directory.GetFiles(directoryPath, "*.gmpk", SearchOption.AllDirectories);
            if (gmpkFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.gmpk文件");
                OnExtractionFailed("未找到.gmpk文件");
                return;
            }
            TotalFilesToExtract = gmpkFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{gmpkFiles.Length}个GMPK文件");
            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    int totalExtractedFiles = 0;
                    foreach (var gmpkFilePath in gmpkFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedCount++;
                        if (processedCount % 10 == 1 || processedCount == gmpkFiles.Length)
                        {
                            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(gmpkFilePath)} ({processedCount}/{gmpkFiles.Length})");
                        }
                        try
                        {
                            string? parentDir = Path.GetDirectoryName(gmpkFilePath);
                            if (string.IsNullOrEmpty(parentDir))
                            {
                                ExtractionError?.Invoke(this, $"无法获取文件目录:{gmpkFilePath}");
                                continue;
                            }

                            byte[] fileData = File.ReadAllBytes(gmpkFilePath);
                            string baseName = Path.GetFileNameWithoutExtension(gmpkFilePath);
                            bool foundG1m = false;
                            bool foundG1t = false;
                            int extractedCount = 0;

                            for (int i = 0; i < fileData.Length; i++)
                            {
                                if (i + 8 >= fileData.Length) break;

                                if (!foundG1m && fileData[i] == 0x5F && fileData[i + 1] == 0x4D && fileData[i + 2] == 0x31 && fileData[i + 3] == 0x47)
                                {
                                    uint fileSize = BitConverter.ToUInt32(fileData, i + 8);
                                    if (fileSize > 0 && i + fileSize <= fileData.Length)
                                    {
                                        string outputPath = Path.Combine(parentDir, $"{baseName}.g1m");
                                        File.WriteAllBytes(outputPath, fileData.Skip(i).Take((int)fileSize).ToArray());
                                        foundG1m = true;
                                        extractedCount++;
                                        totalExtractedFiles++;
                                        OnFileExtracted(outputPath);
                                        i += (int)fileSize - 1;
                                    }
                                }
                                else if (!foundG1t && fileData[i] == 0x47 && fileData[i + 1] == 0x54 && fileData[i + 2] == 0x31 && fileData[i + 3] == 0x47)
                                {
                                    uint fileSize = BitConverter.ToUInt32(fileData, i + 8);
                                    if (fileSize > 0 && i + fileSize <= fileData.Length)
                                    {
                                        string outputPath = Path.Combine(parentDir, $"{baseName}.g1t");
                                        File.WriteAllBytes(outputPath, fileData.Skip(i).Take((int)fileSize).ToArray());
                                        foundG1t = true;
                                        extractedCount++;
                                        totalExtractedFiles++;
                                        OnFileExtracted(outputPath);
                                        i += (int)fileSize - 1;
                                    }
                                }

                                if (foundG1m && foundG1t) break;
                            }

                            ExtractionProgress?.Invoke(this, $"文件{Path.GetFileName(gmpkFilePath)}提取完成,找到{extractedCount}个文件");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(gmpkFilePath)}处理错误:{ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"提取完成,总共生成{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
