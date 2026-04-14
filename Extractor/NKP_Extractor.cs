using System.Text;

namespace super_toolbox
{
    public class NKP_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        private static readonly byte[] NKP_HEADER = Encoding.ASCII.GetBytes("NKP");

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var nkpFiles = Directory.EnumerateFiles(directoryPath, "*.nkp", SearchOption.AllDirectories)
                .Select(f => f.ToLower())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            int processedCount = 0;
            int totalFiles = nkpFiles.Count;

            foreach (var nkpFilePath in nkpFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                processedCount++;
                string fileName = Path.GetFileName(nkpFilePath);
                ExtractionProgress?.Invoke(this, $"正在处理:{fileName} ({processedCount}/{totalFiles})");

                try
                {
                    using (var fs = new FileStream(nkpFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        long fileLength = fs.Length;

                        byte[] headerBuffer = new byte[3];
                        await fs.ReadAsync(headerBuffer, 0, 3, cancellationToken);
                        if (!headerBuffer.SequenceEqual(NKP_HEADER))
                        {
                            ExtractionError?.Invoke(this, $"{fileName}:不是有效的NKP文件");
                            continue;
                        }

                        fs.Position = 0x14;
                        byte[] firstOffsetBuffer = new byte[4];
                        await fs.ReadAsync(firstOffsetBuffer, 0, 4, cancellationToken);
                        long firstFileOffset = BitConverter.ToUInt32(firstOffsetBuffer, 0);

                        fs.Position = 0x10;
                        List<long> fileOffsets = new List<long>();
                        byte[] buffer = new byte[8];
                        long addressIndexEnd = 0;

                        while (fs.Position + 8 <= firstFileOffset)
                        {
                            await fs.ReadAsync(buffer, 0, 8, cancellationToken);
                            long bufferValue = BitConverter.ToInt64(buffer, 0);

                            if (bufferValue == 0x0000000000000000)
                            {
                                addressIndexEnd = fs.Position;
                                break;
                            }

                            long offset = BitConverter.ToUInt32(buffer, 4);
                            if (offset > 0 && offset < fileLength)
                            {
                                fileOffsets.Add(offset);
                            }
                        }

                        fileOffsets = fileOffsets.OrderBy(o => o).ToList();
                        List<(long offset, long size)> fileEntries = new List<(long offset, long size)>();

                        for (int i = 0; i < fileOffsets.Count; i++)
                        {
                            long currentOffset = fileOffsets[i];
                            long nextOffset = (i < fileOffsets.Count - 1) ? fileOffsets[i + 1] : fileLength;
                            long size = nextOffset - currentOffset;
                            fileEntries.Add((currentOffset, size));
                        }

                        List<string> fileNames = new List<string>();
                        if (addressIndexEnd > 0)
                        {
                            long nameStart = addressIndexEnd;
                            long nameEnd = firstFileOffset;
                            int nameSize = (int)(nameEnd - nameStart);

                            if (nameSize > 0)
                            {
                                byte[] nameData = new byte[nameSize];
                                fs.Position = nameStart;
                                await fs.ReadAsync(nameData, 0, nameSize, cancellationToken);

                                int pos = 0;
                                while (pos < nameSize)
                                {
                                    int end = pos;
                                    while (end < nameSize && nameData[end] != 0)
                                    {
                                        end++;
                                    }
                                    if (end > pos)
                                    {
                                        string name = Encoding.ASCII.GetString(nameData, pos, end - pos);
                                        fileNames.Add(name);
                                    }
                                    pos = end + 1;
                                }
                            }
                        }

                        string outputDir = Path.Combine(Path.GetDirectoryName(nkpFilePath) ?? directoryPath, Path.GetFileNameWithoutExtension(nkpFilePath));
                        Directory.CreateDirectory(outputDir);

                        for (int i = 0; i < fileEntries.Count; i++)
                        {
                            var (offset, size) = fileEntries[i];

                            if (i >= fileNames.Count)
                            {
                                ExtractionError?.Invoke(this, $"{fileName}:文件名数量({fileNames.Count})少于文件数量({fileEntries.Count})");
                                break;
                            }

                            string outputName = fileNames[i];
                            string outputPath = Path.Combine(outputDir, outputName);

                            fs.Position = offset;
                            using (var output = new FileStream(outputPath, FileMode.Create))
                            {
                                byte[] data = new byte[65536];
                                long remaining = size;
                                while (remaining > 0)
                                {
                                    int toRead = (int)Math.Min(data.Length, remaining);
                                    int read = await fs.ReadAsync(data, 0, toRead, cancellationToken);
                                    await output.WriteAsync(data, 0, read, cancellationToken);
                                    remaining -= read;
                                }
                            }

                            OnFileExtracted(outputPath);
                            ExtractionProgress?.Invoke(this, $"{fileName}:已提取{outputName}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理{fileName}出错:{ex.Message}");
                }
            }

            OnExtractionCompleted();
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("提取操作已取消", cancellationToken);
        }
    }
}