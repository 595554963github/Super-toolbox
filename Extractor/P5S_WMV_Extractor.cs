using System.Collections.Concurrent;

namespace super_toolbox
{
    public class P5S_WMV_Extractor : BaseExtractor
    {
        private static readonly byte[] WMV_START_SEQ = { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11 };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
               .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
               .ToList();
            TotalFilesToExtract = files.Count;
            var extractedFiles = new ConcurrentBag<string>();
            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            byte[] content = File.ReadAllBytes(filePath);
                            int count = ExtractWmvFiles(content, filePath, extractedDir, extractedFiles);
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"提取文件{filePath} 时发生错误:{ex.Message}");
                        }
                    });
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("提取操作已取消。");
            }
            sw.Stop();
            int actualExtractedCount = Directory.EnumerateFiles(extractedDir, "*.wmv", SearchOption.AllDirectories).Count();
            Console.WriteLine($"处理完成，耗时{sw.Elapsed.TotalSeconds:F2}秒");
            Console.WriteLine($"共提取出{actualExtractedCount}个WMV文件，统计提取文件数量: {ExtractedFileCount}");
            if (ExtractedFileCount != actualExtractedCount)
            {
                Console.WriteLine("警告:统计数量与实际数量不符，可能存在文件操作异常。");
            }
        }
        private int ExtractWmvFiles(byte[] content, string filePath, string extractedDir, ConcurrentBag<string> extractedFiles)
        {
            int count = 0;
            int index = 0;
            while (index < content.Length)
            {
                int headerStartIndex = IndexOf(content, WMV_START_SEQ, index);
                if (headerStartIndex == -1)
                {
                    break;
                }
                int nextHeaderIndex = IndexOf(content, WMV_START_SEQ, headerStartIndex + 1);
                int endIndex = nextHeaderIndex == -1 ? content.Length : nextHeaderIndex;
                byte[] extractedData = new byte[endIndex - headerStartIndex];
                Array.Copy(content, headerStartIndex, extractedData, 0, extractedData.Length);
                string outputFileName = $"{Path.GetFileNameWithoutExtension(filePath)}_{++count}.wmv";
                string outputFilePath = Path.Combine(extractedDir, outputFileName);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
                    File.WriteAllBytes(outputFilePath, extractedData);
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                }
                catch (Exception ex)
                {
                    OnExtractionFailed($"写入文件{outputFilePath} 时发生错误:{ex.Message}");
                }
                index = headerStartIndex + 1;
            }
            return count;
        }

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
    }
}