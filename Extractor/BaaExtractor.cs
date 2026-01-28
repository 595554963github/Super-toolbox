namespace super_toolbox
{
    public class BaaExtractor : BaseExtractor
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
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var baaFiles = Directory.EnumerateFiles(directoryPath, "*.baa", SearchOption.AllDirectories)
                .Where(file => !Directory.Exists(Path.ChangeExtension(file, null)))
                .ToList();

            TotalFilesToExtract = baaFiles.Count;
            int processedFiles = 0;

            foreach (var baaFile in baaFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(baaFile)}");

                try
                {
                    string outputDir = Path.Combine(Path.GetDirectoryName(baaFile) ?? directoryPath,
                                                   $"{Path.GetFileNameWithoutExtension(baaFile)}");
                    Directory.CreateDirectory(outputDir);

                    using (var fileStream = new FileStream(baaFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        await ProcessBaaFile(fileStream, outputDir, extractedFiles, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{baaFile}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{baaFile}时出错:{e.Message}");
                }

                processedFiles++;
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到有效文件");
            }
            OnExtractionCompleted();
        }

        private async Task ProcessBaaFile(FileStream fileStream, string outputDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileNameWithoutExtension(fileStream.Name);

            using (var reader = new BinaryReader(fileStream, System.Text.Encoding.ASCII, true))
            {
                byte[] header = reader.ReadBytes(4);
                if (!CompareBytes(header, new byte[] { (byte)'A', (byte)'A', (byte)'_', (byte)'<' }))
                {
                    ExtractionError?.Invoke(this, $"{fileStream.Name} 不是有效的BAA文件");
                    return;
                }

                int ibnkCount = 0;
                bool continueProcessing = true;

                while (continueProcessing)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    if (fileStream.Position + 4 > fileStream.Length)
                        break;

                    byte[] chunkId = reader.ReadBytes(4);
                    string chunkName = System.Text.Encoding.ASCII.GetString(chunkId);

                    switch (chunkName)
                    {
                        case "bst ":
                            await ProcessBstChunk(reader, fileStream, outputDir, fileName, extractedFiles, cancellationToken);
                            break;
                        case "bstn":
                            await ProcessBstnChunk(reader, fileStream, outputDir, fileName, extractedFiles, cancellationToken);
                            break;
                        case "ws  ":
                            await ProcessWsChunk(reader, fileStream, outputDir, fileName, extractedFiles, cancellationToken);
                            break;
                        case "bnk ":
                            await ProcessBnkChunk(reader, fileStream, outputDir, fileName, ibnkCount, extractedFiles, cancellationToken);
                            ibnkCount++;
                            break;
                        case "bsc ":
                            await ProcessBscChunk(reader, fileStream, outputDir, fileName, extractedFiles, cancellationToken);
                            break;
                        case "bfca":
                            reader.ReadUInt32();
                            break;
                        case ">_AA":
                            continueProcessing = false;
                            break;
                        default:
                            ExtractionError?.Invoke(this, $"未识别的块: {chunkName}");
                            return;
                    }
                }
            }
        }

        private async Task ProcessBstChunk(BinaryReader reader, FileStream stream, string outputDir, string fileName, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int bstOffset = Read32(reader);
            int bstnOffset = Read32(reader);
            int size = bstnOffset - bstOffset;

            if (size <= 0) return;

            string outputPath = Path.Combine(outputDir, $"{fileName}.bst");
            outputPath = GetUniqueFilePath(outputPath, extractedFiles);

            await DumpToFile(stream, outputPath, bstOffset, size, cancellationToken);

            extractedFiles.Add(outputPath);
            OnFileExtracted(outputPath);
            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
        }

        private async Task ProcessBstnChunk(BinaryReader reader, FileStream stream, string outputDir, string fileName, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int bstnOffset = Read32(reader);
            int bstnEndOffset = Read32(reader);
            int size = bstnEndOffset - bstnOffset;

            if (size <= 0) return;

            string outputPath = Path.Combine(outputDir, $"{fileName}.bstn");
            outputPath = GetUniqueFilePath(outputPath, extractedFiles);

            await DumpToFile(stream, outputPath, bstnOffset, size, cancellationToken);

            extractedFiles.Add(outputPath);
            OnFileExtracted(outputPath);
            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
        }

        private async Task ProcessWsChunk(BinaryReader reader, FileStream stream, string outputDir, string fileName, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int wsType = Read32(reader);
            int wsOffset = Read32(reader);
            _ = Read32(reader);

            long currentPos = stream.Position;
            stream.Position = wsOffset + 4;
            int wsSize = Read32(reader);
            stream.Position = currentPos;

            if (wsSize <= 0) return;

            string outputPath = Path.Combine(outputDir, $"{fileName}.{wsType}.wsys");
            outputPath = GetUniqueFilePath(outputPath, extractedFiles);

            await DumpToFile(stream, outputPath, wsOffset, wsSize, cancellationToken);

            extractedFiles.Add(outputPath);
            OnFileExtracted(outputPath);
            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
        }

        private async Task ProcessBnkChunk(BinaryReader reader, FileStream stream, string outputDir, string fileName, int ibnkCount, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int bnkType = Read32(reader);
            int bnkOffset = Read32(reader);

            long currentPos = stream.Position;
            stream.Position = bnkOffset + 4;
            int bnkLen = Read32(reader);
            stream.Position = currentPos;

            if (bnkLen <= 0) return;

            string outputPath = Path.Combine(outputDir, $"{fileName}.{bnkType}_{ibnkCount}.bnk");
            outputPath = GetUniqueFilePath(outputPath, extractedFiles);

            await DumpToFile(stream, outputPath, bnkOffset, bnkLen, cancellationToken);

            extractedFiles.Add(outputPath);
            OnFileExtracted(outputPath);
            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
        }

        private async Task ProcessBscChunk(BinaryReader reader, FileStream stream, string outputDir, string fileName, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int bscOffset = Read32(reader);
            int bscEnd = Read32(reader);
            int size = bscEnd - bscOffset;

            if (size <= 0) return;

            string outputPath = Path.Combine(outputDir, $"{fileName}.bsc");
            outputPath = GetUniqueFilePath(outputPath, extractedFiles);

            await DumpToFile(stream, outputPath, bscOffset, size, cancellationToken);

            extractedFiles.Add(outputPath);
            OnFileExtracted(outputPath);
            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputPath)}");
        }

        private async Task DumpToFile(FileStream inStream, string outputPath, int offset, int size, CancellationToken cancellationToken)
        {
            const int bufferSize = 4096;
            var buffer = new byte[bufferSize];
            long originalPosition = inStream.Position;

            try
            {
                inStream.Position = offset;

                using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true))
                {
                    int bytesRemaining = size;
                    while (bytesRemaining > 0)
                    {
                        int bytesToRead = Math.Min(bufferSize, bytesRemaining);
                        int bytesRead = await inStream.ReadAsync(buffer, 0, bytesToRead, cancellationToken);

                        if (bytesRead == 0) break;

                        await outStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        bytesRemaining -= bytesRead;
                    }
                }
            }
            finally
            {
                inStream.Position = originalPosition;
            }
        }

        private int Read32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private bool CompareBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private string GetUniqueFilePath(string filePath, List<string> extractedFiles)
        {
            if (!File.Exists(filePath) && !extractedFiles.Contains(filePath))
            {
                return filePath;
            }

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);

            int duplicateCount = 1;
            string newFilePath;
            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath) || extractedFiles.Contains(newFilePath));

            return newFilePath;
        }
    }
}
