using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class PngExtractor : BaseExtractor
    {
        private readonly object _lockObject = new object();
        public event EventHandler<string>? ExtractionProgress;

        private static readonly byte[] START_SEQUENCE = { 0x89, 0x50, 0x4E, 0x47 };
        private static readonly byte[] BLOCK_MARKER = { 0x49, 0x48, 0x44, 0x52 };
        private static readonly byte[] END_SEQUENCE = { 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionProgress?.Invoke(this, $"错误: {directoryPath} 不是有效的目录");
                OnExtractionFailed($"错误: {directoryPath} 不是有效的目录");
                return;
            }

            var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            TotalFilesToExtract = files.Length;
            ExtractionProgress?.Invoke(this, $"开始处理 {TotalFilesToExtract} 个文件...");
            OnFileExtracted($"开始处理 {TotalFilesToExtract} 个文件...");

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            if (!Directory.Exists(extractedDir))
            {
                Directory.CreateDirectory(extractedDir);
                ExtractionProgress?.Invoke(this, $"创建文件夹: {extractedDir}");
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    ExtractionProgress?.Invoke(this, $"处理文件 {ExtractedFileCount + 1}/{TotalFilesToExtract}: {Path.GetFileName(file)}");

                    if (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    await ProcessFileAsync(file, extractedDir, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionProgress?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionProgress?.Invoke(this, $"处理文件 {file} 时出错: {ex.Message}");
                    OnExtractionFailed($"处理文件 {file} 时出错: {ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"提取完成: 共找到 {ExtractedFileCount} 个PNG文件");
        }

        private async Task ProcessFileAsync(string filePath, string destinationFolder, CancellationToken cancellationToken)
        {
            const int BufferSize = 8192;
            var startSequenceLength = START_SEQUENCE.Length;
            var endSequenceLength = END_SEQUENCE.Length;

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);

            byte[] buffer = new byte[BufferSize];
            byte[] leftover = Array.Empty<byte>();
            MemoryStream? currentPng = null;
            bool foundStart = false;
            string filePrefix = Path.GetFileNameWithoutExtension(filePath);

            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, BufferSize, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] currentData;
                if (leftover.Length > 0)
                {
                    currentData = new byte[leftover.Length + bytesRead];
                    Array.Copy(leftover, 0, currentData, 0, leftover.Length);
                    Array.Copy(buffer, 0, currentData, leftover.Length, bytesRead);
                }
                else
                {
                    currentData = new byte[bytesRead];
                    Array.Copy(buffer, 0, currentData, 0, bytesRead);
                }

                if (!foundStart)
                {
                    int startIndex = IndexOf(currentData, START_SEQUENCE);
                    if (startIndex != -1)
                    {
                        foundStart = true;
                        currentPng = new MemoryStream();
                        currentPng.Write(currentData, startIndex, currentData.Length - startIndex);

                        leftover = Array.Empty<byte>();
                    }
                    else
                    {
                        leftover = currentData.Length > startSequenceLength
                            ? currentData[^(startSequenceLength - 1)..]
                            : currentData;
                    }
                }
                else
                {
                    currentPng!.Write(currentData, 0, currentData.Length);

                    byte[] pngBytes = currentPng.ToArray();
                    int endIndex = IndexOf(pngBytes, END_SEQUENCE);

                    if (endIndex != -1)
                    {
                        endIndex += endSequenceLength;
                        byte[] extractedData = new byte[endIndex];
                        Array.Copy(pngBytes, 0, extractedData, 0, endIndex);

                        if (ContainsMarker(extractedData, BLOCK_MARKER))
                        {
                            SavePngFile(extractedData, destinationFolder, filePrefix);
                        }

                        foundStart = false;
                        currentPng.Dispose();
                        currentPng = null;

                        if (endIndex < pngBytes.Length)
                        {
                            leftover = pngBytes[endIndex..];
                        }
                        else
                        {
                            leftover = Array.Empty<byte>();
                        }
                    }
                    else
                    {
                        leftover = Array.Empty<byte>();
                    }
                }
            }

            currentPng?.Dispose();
        }

        private void SavePngFile(byte[] pngData, string destinationFolder, string filePrefix)
        {
            lock (_lockObject)
            {
                string newFileName = $"{filePrefix}_{ExtractedFileCount}.png";
                string filePath = Path.Combine(destinationFolder, newFileName);

                try
                {
                    File.WriteAllBytes(filePath, pngData);
                    OnFileExtracted(filePath);
                    ExtractionProgress?.Invoke(this, $"已提取: {newFileName}");
                }
                catch (Exception ex)
                {
                    ExtractionProgress?.Invoke(this, $"保存文件 {newFileName} 时出错: {ex.Message}");
                }
            }
        }

        private static int IndexOf(byte[] source, byte[] pattern)
        {
            for (int i = 0; i <= source.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static bool ContainsMarker(byte[] data, byte[] marker)
        {
            for (int i = 0; i <= data.Length - marker.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < marker.Length; j++)
                {
                    if (data[i + j] != marker[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
            return false;
        }
    }
}