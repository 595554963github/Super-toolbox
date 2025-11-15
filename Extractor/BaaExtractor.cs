namespace super_toolbox
{
    public class BaaExtractor : BaseExtractor
    {
        private const int DumpBufferSize = 512;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            var baaFiles = Directory.GetFiles(directoryPath, "*.baa", SearchOption.AllDirectories)
                .Where(file => !Directory.Exists(Path.ChangeExtension(file, null))) 
                .ToList();

            TotalFilesToExtract = CountTotalFilesToExtract(baaFiles);
            if (TotalFilesToExtract == 0)
            {
                OnExtractionCompleted();
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(baaFiles, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    }, baaFilePath =>
                    {
                        try
                        {
                            ThrowIfCancellationRequested(cancellationToken);
                            ProcessBaaFile(baaFilePath, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"处理{Path.GetFileName(baaFilePath)}时出错: {ex.Message}");
                        }
                    });
                }, cancellationToken);

                if (ExtractedFileCount == TotalFilesToExtract)
                {
                    OnExtractionCompleted();
                }
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取失败: {ex.Message}");
            }
        }

        private int CountTotalFilesToExtract(List<string> baaFiles)
        {
            int count = 0;
            foreach (var baaFilePath in baaFiles)
            {
                try
                {
                    using (var stream = new FileStream(baaFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var reader = new BinaryReader(stream))
                    {
                        byte[] header = reader.ReadBytes(4);
                        if (!CompareBytes(header, new byte[] { (byte)'A', (byte)'A', (byte)'_', (byte)'<' }))
                            continue;

                        int fileCount = 0;
                        bool continueProcessing = true;

                        while (continueProcessing)
                        {
                            if (stream.Position + 4 > stream.Length)
                                break;

                            byte[] chunkId = reader.ReadBytes(4);
                            string chunkName = System.Text.Encoding.ASCII.GetString(chunkId);

                            switch (chunkName)
                            {
                                case "bst ":
                                case "bstn":
                                case "ws  ":
                                case "bsc ":
                                    fileCount++;
                                    break;
                                case "bnk ":
                                    fileCount++; 
                                    break;
                                case "bfca":
                                    break;
                                case ">_AA":
                                    continueProcessing = false;
                                    break;
                            }
                        }
                        count += fileCount;
                    }
                }
                catch
                {
                }
            }
            return count;
        }

        private void ProcessBaaFile(string baaFilePath, CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileNameWithoutExtension(baaFilePath);
            string fileDirectory = Path.GetDirectoryName(baaFilePath)!;
            string extractDir = Path.Combine(fileDirectory, fileName);
            Directory.CreateDirectory(extractDir);

            using (var stream = new FileStream(baaFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                byte[] header = reader.ReadBytes(4);
                if (!CompareBytes(header, new byte[] { (byte)'A', (byte)'A', (byte)'_', (byte)'<' }))
                {
                    OnExtractionFailed($"{baaFilePath} 不是有效的BAA文件");
                    return;
                }

                int ibnkCount = 0;
                bool continueProcessing = true;

                while (continueProcessing)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    if (stream.Position + 4 > stream.Length)
                        break;

                    byte[] chunkId = reader.ReadBytes(4);
                    string chunkName = System.Text.Encoding.ASCII.GetString(chunkId);

                    switch (chunkName)
                    {
                        case "bst ":
                            string bstPath = ProcessBstChunk(reader, stream, extractDir, fileName);
                            OnFileExtracted(bstPath); 
                            break;
                        case "bstn":
                            string bstnPath = ProcessBstnChunk(reader, stream, extractDir, fileName);
                            OnFileExtracted(bstnPath);
                            break;
                        case "ws  ":
                            string wsPath = ProcessWsChunk(reader, stream, extractDir, fileName);
                            OnFileExtracted(wsPath);
                            break;
                        case "bnk ":
                            string bnkPath = ProcessBnkChunk(reader, stream, extractDir, fileName, ref ibnkCount);
                            OnFileExtracted(bnkPath);
                            break;
                        case "bsc ":
                            string bscPath = ProcessBscChunk(reader, stream, extractDir, fileName);
                            OnFileExtracted(bscPath);
                            break;
                        case "bfca":
                            ProcessBfcaChunk(reader);
                            break;
                        case ">_AA":
                            continueProcessing = false;
                            break;
                        default:
                            OnExtractionFailed($"未识别的块: {chunkName}");
                            return;
                    }
                }
            }
        }

        private string ProcessBstChunk(BinaryReader reader, FileStream stream, string extractDir, string fileName)
        {
            int bstOffset = Read32(reader);
            int bstnOffset = Read32(reader);
            int size = bstnOffset - bstOffset;

            string outputPath = Path.Combine(extractDir, $"{fileName}.bst");
            DumpToFile(stream, outputPath, bstOffset, size);
            return outputPath; 
        }

        private string ProcessBstnChunk(BinaryReader reader, FileStream stream, string extractDir, string fileName)
        {
            int bstnOffset = Read32(reader);
            int bstnEndOffset = Read32(reader);
            int size = bstnEndOffset - bstnOffset;

            string outputPath = Path.Combine(extractDir, $"{fileName}.bstn");
            DumpToFile(stream, outputPath, bstnOffset, size);
            return outputPath;
        }

        private string ProcessWsChunk(BinaryReader reader, FileStream stream, string extractDir, string fileName)
        {
            int wsType = Read32(reader);
            int wsOffset = Read32(reader);
            _ = Read32(reader); 

            long currentPos = stream.Position;
            stream.Position = wsOffset + 4;
            int wsSize = Read32(reader);
            stream.Position = currentPos;

            string outputPath = Path.Combine(extractDir, $"{fileName}.{wsType}.wsys");
            DumpToFile(stream, outputPath, wsOffset, wsSize);
            return outputPath;
        }

        private string ProcessBnkChunk(BinaryReader reader, FileStream stream, string extractDir, string fileName, ref int ibnkCount)
        {
            int bnkType = Read32(reader);
            int bnkOffset = Read32(reader);

            long currentPos = stream.Position;
            stream.Position = bnkOffset + 4;
            int bnkLen = Read32(reader);
            stream.Position = currentPos;

            string outputPath = Path.Combine(extractDir, $"{fileName}.{bnkType}_{ibnkCount++}.bnk");
            DumpToFile(stream, outputPath, bnkOffset, bnkLen);
            return outputPath;
        }

        private string ProcessBscChunk(BinaryReader reader, FileStream stream, string extractDir, string fileName)
        {
            int bscOffset = Read32(reader);
            int bscEnd = Read32(reader);
            int size = bscEnd - bscOffset;

            string outputPath = Path.Combine(extractDir, $"{fileName}.bsc");
            DumpToFile(stream, outputPath, bscOffset, size);
            return outputPath;
        }

        private void ProcessBfcaChunk(BinaryReader reader)
        {
            _ = Read32(reader);
        }

        private void DumpToFile(FileStream inStream, string outputPath, int offset, int size)
        {
            if (size <= 0) return;

            var buffer = new byte[DumpBufferSize];
            int bytesRead;
            long originalPosition = inStream.Position;

            try
            {
                inStream.Position = offset;

                using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    while (size > 0)
                    {
                        bytesRead = inStream.Read(buffer, 0, Math.Min(size, DumpBufferSize));
                        if (bytesRead == 0) break;

                        outStream.Write(buffer, 0, bytesRead);
                        size -= bytesRead;
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

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}