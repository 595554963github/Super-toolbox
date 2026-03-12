using System.Collections.Concurrent;

namespace super_toolbox
{
    public class LopusExtractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private static readonly LopusFormatInfo[] FormatInfos = new LopusFormatInfo[]
        {
            new LopusFormatInfo(
                name: "LOPUS",
                signature: new byte[] { 0x01, 0x00, 0x00, 0x80, 0x18, 0x00, 0x00, 0x00 },
                sizeOffset: 0x24,
                headerSize: 40
            )
        };

        private ConcurrentDictionary<string, int> _counters = new ConcurrentDictionary<string, int>();

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

            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = files.Count;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}，共找到{files.Count}个文件");

            foreach (var format in FormatInfos)
            {
                _counters[format.Name] = 0;
            }

            try
            {
                int totalExtractedFiles = 0;

                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            byte[] content = File.ReadAllBytes(filePath);
                            int extracted = ExtractLopusFormat(filePath, content, FormatInfos[0], cancellationToken);
                            Interlocked.Add(ref totalExtractedFiles, extracted);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                        }
                    });
                }, cancellationToken);

                int totalExtracted = _counters.Sum(x => x.Value);
                if (totalExtracted > 0)
                {
                    ExtractionProgress?.Invoke(this, $"处理完成，共提取出{totalExtracted}个.lopus文件");
                }
                else
                {
                    ExtractionProgress?.Invoke(this, "处理完成，未找到LOPUS格式");
                }
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
                throw;
            }
        }

        private int ExtractLopusFormat(string filePath, byte[] content, LopusFormatInfo format, CancellationToken cancellationToken)
        {
            int index = 0;
            string sourceDir = Path.GetDirectoryName(filePath) ?? "";
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            int extractedCount = 0;
            int fileCounter = 0;

            while (index <= content.Length - format.Signature.Length)
            {
                ThrowIfCancellationRequested(cancellationToken);

                int startIndex = IndexOf(content, format.Signature, index);
                if (startIndex == -1) break;

                if (startIndex + format.SizeOffset + 4 > content.Length)
                {
                    index = startIndex + 1;
                    continue;
                }

                int bodySize = BitConverter.ToInt32(content, startIndex + format.SizeOffset);
                int totalSize = format.HeaderSize + bodySize;

                if (bodySize <= 0 || startIndex + totalSize > content.Length || totalSize > 100 * 1024 * 1024)
                {
                    index = startIndex + 1;
                    continue;
                }

                byte[] lopusData = new byte[totalSize];
                Array.Copy(content, startIndex, lopusData, 0, totalSize);

                int counterValue = _counters.AddOrUpdate(format.Name, 1, (key, oldValue) => oldValue + 1);
                fileCounter++;

                string lopusFileName;
                if (fileCounter == 1 && extractedCount == 0)
                {
                    lopusFileName = $"{baseFileName}.lopus";
                }
                else
                {
                    lopusFileName = $"{baseFileName}_{fileCounter}.lopus";
                }

                string lopusFilePath = Path.Combine(sourceDir, lopusFileName);
                lopusFilePath = MakeUniqueFilename(lopusFilePath);

                File.WriteAllBytes(lopusFilePath, lopusData);
                OnFileExtracted(lopusFilePath);
                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(lopusFilePath)}");

                extractedCount++;
                index = startIndex + totalSize;
            }

            return extractedCount;
        }

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            if (data == null || pattern == null || startIndex < 0 || startIndex > data.Length - pattern.Length)
                return -1;

            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private string MakeUniqueFilename(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int counter = 1;

            while (true)
            {
                string newPath = Path.Combine(directory, $"{fileName}_{counter}{extension}");
                if (!File.Exists(newPath))
                    return newPath;
                counter++;
            }
        }

        private class LopusFormatInfo
        {
            public string Name { get; }
            public byte[] Signature { get; }
            public int SizeOffset { get; }
            public int HeaderSize { get; }

            public LopusFormatInfo(string name, byte[] signature, int sizeOffset, int headerSize)
            {
                Name = name;
                Signature = signature;
                SizeOffset = sizeOffset;
                HeaderSize = headerSize;
            }
        }
    }
}