using System.ComponentModel;
using System.Diagnostics;

namespace super_toolbox
{
    public class WaveExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static readonly byte[] RIFF_HEADER = { 0x52, 0x49, 0x46, 0x46 };
        private static readonly byte[] AUDIO_BLOCK = { 0x57, 0x41, 0x56, 0x45, 0x66, 0x6D, 0x74 };
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
                    int? currentWaveStart = null;
                    int waveCount = 1;
                    while (index < content.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int waveStartIndex = IndexOf(content, RIFF_HEADER, index);
                        if (waveStartIndex == -1)
                        {
                            if (currentWaveStart.HasValue)
                            {
                                if (ProcessWaveSegment(content, currentWaveStart.Value, content.Length,
                                                    filePath, waveCount, extractedDir, extractedFiles))
                                {
                                    totalExtractedFiles++;
                                }
                                waveCount++;
                            }
                            break;
                        }
                        if (IsValidWaveHeader(content, waveStartIndex))
                        {
                            if (!currentWaveStart.HasValue)
                            {
                                currentWaveStart = waveStartIndex;
                            }
                            else
                            {
                                if (ProcessWaveSegment(content, currentWaveStart.Value, waveStartIndex,
                                                    filePath, waveCount, extractedDir, extractedFiles))
                                {
                                    totalExtractedFiles++;
                                }
                                waveCount++;
                                currentWaveStart = waveStartIndex;
                            }
                        }
                        index = waveStartIndex + 1;
                    }
                    if (currentWaveStart.HasValue)
                    {
                        if (ProcessWaveSegment(content, currentWaveStart.Value, content.Length,
                                            filePath, waveCount, extractedDir, extractedFiles))
                        {
                            totalExtractedFiles++;
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
                ExtractionProgress?.Invoke(this, $"处理完成，共处理{totalSourceFiles}个源文件，提取出{totalExtractedFiles}个音频文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共处理{totalSourceFiles}个源文件，未找到音频文件");
            }
            OnExtractionCompleted();
        }
        private bool IsValidWaveHeader(byte[] content, int startIndex)
        {
            if (startIndex + 12 >= content.Length)
                return false;
            int fileSize = BitConverter.ToInt32(content, startIndex + 4);
            if (fileSize <= 0 || startIndex + 8 + fileSize > content.Length)
                return false;
            int blockStart = startIndex + 8;
            return IndexOf(content, AUDIO_BLOCK, blockStart) != -1;
        }
        private bool ProcessWaveSegment(byte[] content, int start, int end, string filePath, int waveCount,
                              string extractedDir, List<string> extractedFiles)
        {
            int length = end - start;
            if (length <= RIFF_HEADER.Length)
                return false;
            int actualLength = Math.Min(length, content.Length - start);
            byte[] waveData = new byte[actualLength];
            Array.Copy(content, start, waveData, 0, actualLength);
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            string tempFileName = $"{baseFileName}_{waveCount}.temp";
            string tempFilePath = Path.Combine(extractedDir, tempFileName);
            try
            {
                File.WriteAllBytes(tempFilePath, waveData);
                string detectedExtension = AnalyzeAudioFormat(tempFilePath);
                string outputFileName = $"{baseFileName}_{waveCount}.{detectedExtension}";
                string outputFilePath = Path.Combine(extractedDir, outputFileName);
                if (File.Exists(outputFilePath))
                {
                    int duplicateCount = 1;
                    do
                    {
                        outputFileName = $"{baseFileName}_{waveCount}_dup{duplicateCount}.{detectedExtension}";
                        outputFilePath = Path.Combine(extractedDir, outputFileName);
                        duplicateCount++;
                    } while (File.Exists(outputFilePath));
                }
                File.Move(tempFilePath, outputFilePath);
                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath); 
                    return true;
                }
            }
            catch (Exception)
            {
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
                throw;
            }
            return false;
        }
        private string AnalyzeAudioFormat(string filePath)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{filePath}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (output.Contains("atrac3", StringComparison.OrdinalIgnoreCase))
                    {
                        return "at3";
                    }
                    else if (output.Contains("atrac9", StringComparison.OrdinalIgnoreCase))
                    {
                        return "at9";
                    }
                    else if (output.Contains("xma2", StringComparison.OrdinalIgnoreCase))
                    {
                        return "xma";
                    }
                    else if (output.Contains("none", StringComparison.OrdinalIgnoreCase))
                    {
                        return "wem";
                    }
                    else if (output.Contains("pcm_s8", StringComparison.OrdinalIgnoreCase) ||
                             output.Contains("pcm_s16le", StringComparison.OrdinalIgnoreCase) ||
                             output.Contains("pcm_s16be", StringComparison.OrdinalIgnoreCase) ||
                             output.Contains("pcm_s24le", StringComparison.OrdinalIgnoreCase) ||
                             output.Contains("pcm_s24be", StringComparison.OrdinalIgnoreCase) ||
                             output.Contains("pcm_s32le", StringComparison.OrdinalIgnoreCase) ||
                             output.Contains("pcm_s32be", StringComparison.OrdinalIgnoreCase))
                    {
                        return "wav";
                    }
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is Win32Exception)
            {
                ExtractionProgress?.Invoke(this, "警告: FFmpeg未安装，默认使用WAV格式");
            }
            return "wav";
        }
    }
}