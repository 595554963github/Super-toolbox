namespace super_toolbox
{
    public class Radgametools_Audio_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] ADAR_SIGNATURE = { 0x41, 0x44, 0x41, 0x52 };
        private static readonly byte[] SEEK_SIGNATURE = { 0x53, 0x45, 0x45, 0x4B };
        private static readonly byte[] ABEU_SIGNATURE = { 0x41, 0x42, 0x45, 0x55 };
        private const int RADA_HEADER_SIZE = 168;

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

        private static bool ContainsBytes(byte[] data, byte[] pattern)
        {
            return IndexOf(data, pattern, 0) != -1;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.uasset", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
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
                        string ubulkPath = Path.Combine(Path.GetDirectoryName(uassetPath) ?? "", baseFileName + ".ubulk");

                        if (!File.Exists(ubulkPath))
                        {
                            ExtractionProgress?.Invoke(this, $"错误:缺少对应的.ubulk文件:{baseFileName}");
                            continue;
                        }

                        bool isRada = await ProcessRadaFileAsync(uassetPath, ubulkPath, extractedDir, extractedFiles, baseFileName, cancellationToken);

                        if (!isRada)
                        {
                            await ProcessBinkaFileAsync(uassetPath, ubulkPath, extractedDir, extractedFiles, baseFileName, cancellationToken);
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

        private async Task<bool> ProcessRadaFileAsync(string uassetPath, string ubulkPath, string extractedDir,
                                                     List<string> extractedFiles, string baseFileName, CancellationToken cancellationToken)
        {
            byte[]? radaHeader = await ExtractRadaHeaderAsync(uassetPath, cancellationToken);
            if (radaHeader == null)
            {
                return false;
            }

            bool isValidUbulk = await VerifyUbulkHeaderAsync(ubulkPath, cancellationToken);
            if (!isValidUbulk)
            {
                ExtractionProgress?.Invoke(this, $"错误:UBULK文件头部无效:{baseFileName}");
                return false;
            }

            bool success = await CreateRadaFileAsync(uassetPath, radaHeader, ubulkPath, extractedDir, extractedFiles, cancellationToken);
            if (success)
            {
                ExtractionProgress?.Invoke(this, $"成功创建{baseFileName}.rada");
                return true;
            }

            return false;
        }

        private async Task<bool> ProcessBinkaFileAsync(string uassetPath, string ubulkPath, string extractedDir,
                                                      List<string> extractedFiles, string baseFileName, CancellationToken cancellationToken)
        {
            byte[]? uassetSection = await ExtractAbeuSectionAsync(uassetPath, cancellationToken);
            if (uassetSection == null)
            {
                ExtractionProgress?.Invoke(this, $"错误:在UASSET文件中未找到ABEU签名:{baseFileName}");
                return false;
            }

            bool success = await CreateBinkaFileAsync(uassetSection, ubulkPath, extractedDir, extractedFiles, baseFileName, cancellationToken);
            if (success)
            {
                ExtractionProgress?.Invoke(this, $"成功创建{baseFileName}.binka");
                return true;
            }

            return false;
        }

        private async Task<byte[]?> ExtractRadaHeaderAsync(string uassetPath, CancellationToken cancellationToken)
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

        private async Task<byte[]?> ExtractAbeuSectionAsync(string uassetPath, CancellationToken cancellationToken)
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
                byte[] uassetSection = new byte[remainingLength];
                Array.Copy(data, abeuPos, uassetSection, 0, remainingLength);

                return uassetSection;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> VerifyUbulkHeaderAsync(string ubulkPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] header = new byte[4];
                using (var fs = new FileStream(ubulkPath, FileMode.Open, FileAccess.Read))
                {
                    await fs.ReadAsync(header, 0, 4, cancellationToken);
                }
                return header.SequenceEqual(SEEK_SIGNATURE);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CreateRadaFileAsync(string uassetPath, byte[] radaHeader, string ubulkPath,
                                                   string extractedDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            try
            {
                string baseFileName = Path.GetFileNameWithoutExtension(uassetPath);
                byte[] ubulkData = await File.ReadAllBytesAsync(ubulkPath, cancellationToken);

                byte[] radaData = new byte[radaHeader.Length + ubulkData.Length];
                Array.Copy(radaHeader, 0, radaData, 0, radaHeader.Length);
                Array.Copy(ubulkData, 0, radaData, radaHeader.Length, ubulkData.Length);

                string outputFileName = $"{baseFileName}.rada";
                string outputFilePath = Path.Combine(extractedDir, outputFileName);

                outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                await File.WriteAllBytesAsync(outputFilePath, radaData, cancellationToken);

                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"创建RADA文件时出错:{ex.Message}");
                OnExtractionFailed($"创建RADA文件时出错:{ex.Message}");
            }

            return false;
        }

        private async Task<bool> CreateBinkaFileAsync(byte[] uassetSection, string ubulkPath,
                                                     string extractedDir, List<string> extractedFiles,
                                                     string baseFileName, CancellationToken cancellationToken)
        {
            try
            {
                byte[] ubulkData = await File.ReadAllBytesAsync(ubulkPath, cancellationToken);

                byte[] binkaData = new byte[uassetSection.Length + ubulkData.Length];
                Array.Copy(uassetSection, 0, binkaData, 0, uassetSection.Length);
                Array.Copy(ubulkData, 0, binkaData, uassetSection.Length, ubulkData.Length);

                string outputFileName = $"{baseFileName}.binka";
                string outputFilePath = Path.Combine(extractedDir, outputFileName);

                outputFilePath = await GenerateUniqueFilePathAsync(outputFilePath, cancellationToken);

                await File.WriteAllBytesAsync(outputFilePath, binkaData, cancellationToken);

                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"创建BINKA文件时出错:{ex.Message}");
                OnExtractionFailed($"创建BINKA文件时出错:{ex.Message}");
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
