namespace super_toolbox
{
    public class Radgametools_Audio_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private static readonly byte[] ADAR_SIGNATURE = { 0x41, 0x44, 0x41, 0x52 };
        private static readonly byte[] ABEU_SIGNATURE = { 0x41, 0x42, 0x45, 0x55 };
        private const int RADA_HEADER_SIZE = 168;
        private const int ABEU_UEXP_SIZE = 48;

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
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

        private static int LastIndexOf(byte[] data, byte[] pattern)
        {
            for (int i = data.Length - pattern.Length; i >= 0; i--)
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

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.uasset", SearchOption.AllDirectories)
                .ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            try
            {
                foreach (var uassetPath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(uassetPath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        string baseFileName = Path.GetFileNameWithoutExtension(uassetPath);
                        string directory = Path.GetDirectoryName(uassetPath) ?? "";
                        string ubulkPath = Path.Combine(directory, baseFileName + ".ubulk");
                        string uexpPath = Path.Combine(directory, baseFileName + ".uexp");

                        if (!File.Exists(ubulkPath))
                        {
                            ExtractionProgress?.Invoke(this, $"错误:缺少对应的.ubulk文件:{baseFileName}");
                            continue;
                        }

                        bool hasUexp = File.Exists(uexpPath);

                        if (hasUexp)
                        {
                            await ProcessWithUexpAsync(uexpPath, ubulkPath, directory, extractedFiles, baseFileName, cancellationToken);
                        }
                        else
                        {
                            await ProcessWithUassetAsync(uassetPath, ubulkPath, directory, extractedFiles, baseFileName, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{uassetPath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{uassetPath}时出错:{ex.Message}");
                    }
                }

                if (extractedFiles.Count > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成,共创建{extractedFiles.Count}个音频文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成,未创建任何音频文件");
                }

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ProcessWithUassetAsync(string uassetPath, string ubulkPath, string outputDir,
                                                 List<string> extractedFiles, string baseFileName, CancellationToken cancellationToken)
        {
            byte[]? radaHeader = await ExtractRadaHeaderFromUassetAsync(uassetPath, cancellationToken);
            if (radaHeader != null)
            {
                bool success = await CreateAudioFileAsync(radaHeader, ubulkPath, outputDir, extractedFiles,
                                                          baseFileName, ".rada", cancellationToken);
                if (success)
                {
                    ExtractionProgress?.Invoke(this, $"成功创建{baseFileName}.rada");
                    return;
                }
            }

            byte[]? abeuSection = await ExtractAbeuFromUassetAsync(uassetPath, cancellationToken);
            if (abeuSection != null)
            {
                bool success = await CreateAudioFileAsync(abeuSection, ubulkPath, outputDir, extractedFiles,
                                                          baseFileName, ".binka", cancellationToken);
                if (success)
                {
                    ExtractionProgress?.Invoke(this, $"成功创建{baseFileName}.binka");
                    return;
                }
            }

            ExtractionProgress?.Invoke(this, $"错误:在UASSET文件中未找到ADAR或ABEU签名:{baseFileName}");
        }

        private async Task ProcessWithUexpAsync(string uexpPath, string ubulkPath, string outputDir,
                                               List<string> extractedFiles, string baseFileName, CancellationToken cancellationToken)
        {
            byte[]? radaHeader = await ExtractRadaHeaderFromUexpAsync(uexpPath, cancellationToken);
            if (radaHeader != null)
            {
                bool success = await CreateAudioFileAsync(radaHeader, ubulkPath, outputDir, extractedFiles,
                                                          baseFileName, ".rada", cancellationToken);
                if (success)
                {
                    ExtractionProgress?.Invoke(this, $"成功创建{baseFileName}.rada");
                    return;
                }
            }

            byte[]? abeuHeader = await ExtractAbeuHeaderFromUexpAsync(uexpPath, cancellationToken);
            if (abeuHeader != null)
            {
                bool success = await CreateAudioFileAsync(abeuHeader, ubulkPath, outputDir, extractedFiles,
                                                          baseFileName, ".binka", cancellationToken);
                if (success)
                {
                    ExtractionProgress?.Invoke(this, $"成功创建{baseFileName}.binka");
                    return;
                }
            }

            ExtractionProgress?.Invoke(this, $"错误:在UEXP文件中未找到ADAR或ABEU签名:{baseFileName}");
        }

        private async Task<byte[]?> ExtractRadaHeaderFromUassetAsync(string uassetPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] data = await File.ReadAllBytesAsync(uassetPath, cancellationToken);

                int adarPos = IndexOf(data, ADAR_SIGNATURE, 0);
                if (adarPos == -1)
                {
                    return null;
                }

                if (adarPos + RADA_HEADER_SIZE > data.Length)
                {
                    return null;
                }

                byte[] radaHeader = new byte[RADA_HEADER_SIZE];
                Array.Copy(data, adarPos, radaHeader, 0, RADA_HEADER_SIZE);

                return radaHeader;
            }
            catch
            {
                return null;
            }
        }

        private async Task<byte[]?> ExtractRadaHeaderFromUexpAsync(string uexpPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] data = await File.ReadAllBytesAsync(uexpPath, cancellationToken);

                int adarPos = IndexOf(data, ADAR_SIGNATURE, 0);
                if (adarPos == -1)
                {
                    return null;
                }

                if (adarPos + RADA_HEADER_SIZE > data.Length)
                {
                    return null;
                }

                byte[] radaHeader = new byte[RADA_HEADER_SIZE];
                Array.Copy(data, adarPos, radaHeader, 0, RADA_HEADER_SIZE);

                return radaHeader;
            }
            catch
            {
                return null;
            }
        }

        private async Task<byte[]?> ExtractAbeuFromUassetAsync(string uassetPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] data = await File.ReadAllBytesAsync(uassetPath, cancellationToken);

                int abeuPos = LastIndexOf(data, ABEU_SIGNATURE);
                if (abeuPos == -1)
                {
                    return null;
                }

                int remainingLength = data.Length - abeuPos;
                byte[] abeuSection = new byte[remainingLength];
                Array.Copy(data, abeuPos, abeuSection, 0, remainingLength);

                return abeuSection;
            }
            catch
            {
                return null;
            }
        }

        private async Task<byte[]?> ExtractAbeuHeaderFromUexpAsync(string uexpPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] data = await File.ReadAllBytesAsync(uexpPath, cancellationToken);

                int abeuPos = IndexOf(data, ABEU_SIGNATURE, 0);
                if (abeuPos == -1)
                {
                    return null;
                }

                if (abeuPos + ABEU_UEXP_SIZE > data.Length)
                {
                    return null;
                }

                byte[] abeuHeader = new byte[ABEU_UEXP_SIZE];
                Array.Copy(data, abeuPos, abeuHeader, 0, ABEU_UEXP_SIZE);

                return abeuHeader;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> CreateAudioFileAsync(byte[] headerSection, string ubulkPath, string outputDir,
                                                     List<string> extractedFiles, string baseFileName,
                                                     string extension, CancellationToken cancellationToken)
        {
            try
            {
                byte[] ubulkData = await File.ReadAllBytesAsync(ubulkPath, cancellationToken);

                byte[] audioData = new byte[headerSection.Length + ubulkData.Length];
                Array.Copy(headerSection, 0, audioData, 0, headerSection.Length);
                Array.Copy(ubulkData, 0, audioData, headerSection.Length, ubulkData.Length);

                string outputFileName = $"{baseFileName}{extension}";
                string outputFilePath = Path.Combine(outputDir, outputFileName);

                outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                await File.WriteAllBytesAsync(outputFilePath, audioData, cancellationToken);

                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"创建音频文件时出错:{ex.Message}");
                OnExtractionFailed($"创建音频文件时出错:{ex.Message}");
            }

            return false;
        }

        private async Task<string> GenerateUniqueFilePathAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            int counter = 1;
            string newPath;

            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
                counter++;
                ThrowIfCancellationRequested(cancellationToken);
            }
            while (File.Exists(newPath));

            return newPath;
        }
    }
}
