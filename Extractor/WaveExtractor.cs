namespace super_toolbox
{
    public class WaveExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] RIFF_HEADER = { 0x52, 0x49, 0x46, 0x46 };

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
            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();
            int totalSourceFiles = filePaths.Count;
            int processedSourceFiles = 0;
            int totalExtractedFiles = 0;
            TotalFilesToExtract = totalSourceFiles;
            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedSourceFiles++;
                ExtractionProgress?.Invoke(this, $"正在处理源文件({processedSourceFiles}/{totalSourceFiles}):{Path.GetFileName(filePath)}");
                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                    int index = 0;
                    int waveCount = 0;
                    while (index < content.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int riffStart = IndexOf(content, RIFF_HEADER, index);
                        if (riffStart == -1) break;
                        if (IsValidWaveHeader(content, riffStart))
                        {
                            waveCount++;
                            int fileSize = BitConverter.ToInt32(content, riffStart + 4);
                            int waveEnd = riffStart + 8 + fileSize;
                            if (waveEnd > content.Length)
                                waveEnd = content.Length;
                            if (ExtractWaveFile(content, riffStart, waveEnd, filePath, waveCount, extractedDir, extractedFiles))
                            {
                                totalExtractedFiles++;
                            }
                            index = waveEnd;
                        }
                        else
                        {
                            index = riffStart + 4;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (IOException e)
                {
                    ExtractionError?.Invoke(this, $"读取文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"读取文件{filePath}时出错:{e.Message}");
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{filePath}时发生错误:{e.Message}");
                    OnExtractionFailed($"处理文件{filePath}时发生错误:{e.Message}");
                }
            }
            if (totalExtractedFiles > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共处理{totalSourceFiles}个源文件,提取出{totalExtractedFiles}个音频文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共处理{totalSourceFiles}个源文件,未找到音频文件");
            }
            OnExtractionCompleted();
        }
        private bool IsValidWaveHeader(byte[] content, int startIndex)
        {
            if (startIndex + 12 >= content.Length)
                return false;
            if (content[startIndex + 8] != 0x57 || content[startIndex + 9] != 0x41 ||
                content[startIndex + 10] != 0x56 || content[startIndex + 11] != 0x45)
                return false;
            int fileSize = BitConverter.ToInt32(content, startIndex + 4);
            if (fileSize <= 0 || startIndex + 8 + fileSize > content.Length)
                return false;
            return true;
        }
        private bool ExtractWaveFile(byte[] content, int start, int end, string filePath, int waveCount,
                              string extractedDir, List<string> extractedFiles)
        {
            int length = end - start;
            if (length <= 8) return false;
            byte[] waveData = new byte[length];
            Array.Copy(content, start, waveData, 0, length);
            string extension = IdentifyAudioFormat(waveData);
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string outputFileName = $"{baseFileName}_{waveCount}.{extension}";
            string outputFilePath = Path.Combine(extractedDir, outputFileName);
            if (File.Exists(outputFilePath))
            {
                int duplicateCount = 1;
                do
                {
                    outputFileName = $"{baseFileName}_{waveCount}_dup{duplicateCount}.{extension}";
                    outputFilePath = Path.Combine(extractedDir, outputFileName);
                    duplicateCount++;
                } while (File.Exists(outputFilePath));
            }
            try
            {
                File.WriteAllBytes(outputFilePath, waveData);
                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFilePath)} (格式:{extension})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"保存文件{outputFileName}时出错:{ex.Message}");
                return false;
            }
            return false;
        }
        private string IdentifyAudioFormat(byte[] waveData)
        {
            if (waveData.Length < 0x18) return "wav";
            if (waveData[0x10] == 0x20 && waveData[0x14] == 0x70 && waveData[0x15] == 0x02)
                return "at3";
            if (waveData[0x10] == 0x34 && waveData[0x14] == 0xFE && waveData[0x15] == 0xFF)
                return "at9";
            if (waveData[0x10] == 0x34 && waveData[0x14] == 0x66 && waveData[0x15] == 0x01)
                return "xma";
            if (waveData[0x10] == 0x42 && waveData[0x14] == 0xFF && waveData[0x15] == 0xFF)
                return "wem";
            if (waveData[0x10] == 0x10 && waveData[0x14] == 0x01 && waveData[0x15] == 0x00)
                return "wav";
            return "wav";
        }
    }
}
