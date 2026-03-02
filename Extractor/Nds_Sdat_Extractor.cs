namespace super_toolbox
{
    public class Nds_Sdat_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[][] Signatures = new byte[][]
        {
            new byte[] { 0x53, 0x53, 0x45, 0x51, 0xFF, 0xFE, 0x00, 0x01 },
            new byte[] { 0x53, 0x42, 0x4E, 0x4B, 0xFF, 0xFE, 0x00, 0x01 },
            new byte[] { 0x53, 0x57, 0x41, 0x52, 0xFF, 0xFE, 0x00, 0x01 }
        };

        private static readonly string[] Extensions = new string[]
        {
            ".sseq",
            ".sbnk",
            ".swar"
        };

        private int _totalFilesExtracted = 0;
        private int _totalSwavExtracted = 0;

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

            var files = Directory.EnumerateFiles(directoryPath, "*.sdat", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = files.Count;
            ExtractionStarted?.Invoke(this, $"开始处理SDAT文件，共找到{files.Count}个文件");

            _totalFilesExtracted = 0;
            _totalSwavExtracted = 0;

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            string outputDir = Path.Combine(Path.GetDirectoryName(filePath) ?? "", Path.GetFileNameWithoutExtension(filePath));
                            Directory.CreateDirectory(outputDir);

                            byte[] content = File.ReadAllBytes(filePath);
                            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                            int extracted = ExtractFromFile(content, baseFileName, outputDir, cancellationToken);
                            Interlocked.Add(ref _totalFilesExtracted, extracted);
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

                int totalAll = _totalFilesExtracted + _totalSwavExtracted;
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{totalAll}个文件(SSEQ/SBNK/SWAR:{_totalFilesExtracted},SWAV:{_totalSwavExtracted})");
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

        private int ExtractFromFile(byte[] content, string baseFileName, string outputDir, CancellationToken cancellationToken)
        {
            int extractedCount = 0;
            int fileCounter = 1;
            List<string> swarFiles = new List<string>();

            for (int sigIdx = 0; sigIdx < Signatures.Length; sigIdx++)
            {
                byte[] signature = Signatures[sigIdx];
                string extension = Extensions[sigIdx];
                string formatDir = Path.Combine(outputDir, extension.TrimStart('.').ToUpper());
                Directory.CreateDirectory(formatDir);

                int index = 0;
                while (index <= content.Length - signature.Length)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    int startIndex = FindSignature(content, signature, index);
                    if (startIndex == -1) break;

                    if (startIndex + 12 > content.Length) break;

                    int fileSize = BitConverter.ToInt32(content, startIndex + 8);

                    if (fileSize <= 0 || startIndex + fileSize > content.Length || fileSize > 100 * 1024 * 1024)
                    {
                        index = startIndex + 1;
                        continue;
                    }

                    byte[] fileData = new byte[fileSize];
                    Array.Copy(content, startIndex, fileData, 0, fileSize);

                    string fileName = $"{baseFileName}_{fileCounter++}{extension}";
                    string filePath = Path.Combine(formatDir, fileName);
                    filePath = GetUniqueFilePath(filePath);

                    File.WriteAllBytes(filePath, fileData);
                    OnFileExtracted(filePath);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(filePath)}");

                    if (extension == ".swar")
                    {
                        swarFiles.Add(filePath);
                    }

                    extractedCount++;
                    index = startIndex + fileSize;
                }
            }

            foreach (string swarFile in swarFiles)
            {
                int swavCount = ExtractSwavFromSwar(swarFile, cancellationToken);
                if (swavCount > 0)
                {
                    Interlocked.Add(ref _totalSwavExtracted, swavCount);
                    ExtractionProgress?.Invoke(this, $"从{Path.GetFileName(swarFile)}提取出{swavCount}个SWAV文件");
                }
            }

            return extractedCount;
        }

        private int ExtractSwavFromSwar(string swarFilePath, CancellationToken cancellationToken)
        {
            ThrowIfCancellationRequested(cancellationToken);

            string outputDir = Path.Combine(Path.GetDirectoryName(swarFilePath) ?? "", Path.GetFileNameWithoutExtension(swarFilePath));
            Directory.CreateDirectory(outputDir);

            using (FileStream fs = new FileStream(swarFilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                byte[] magic = reader.ReadBytes(8);
                if (!magic.SequenceEqual(new byte[] { 0x53, 0x57, 0x41, 0x52, 0xFF, 0xFE, 0x00, 0x01 }))
                {
                    return 0;
                }

                reader.ReadUInt32();
                reader.ReadUInt16();
                reader.ReadUInt16();

                reader.ReadBytes(4);
                reader.ReadUInt32();
                reader.ReadBytes(0x20);
                int numSamples = reader.ReadInt32();

                int[] sampleOffsets = new int[numSamples];
                for (int i = 0; i < numSamples; i++)
                {
                    sampleOffsets[i] = reader.ReadInt32();
                }

                int extractedCount = 0;
                for (int i = 0; i < numSamples; i++)
                {
                    if (sampleOffsets[i] == 0) continue;

                    long currentPos = fs.Position;
                    fs.Seek(sampleOffsets[i], SeekOrigin.Begin);

                    byte waveType = reader.ReadByte();
                    byte loopFlag = reader.ReadByte();
                    ushort sampleRate = reader.ReadUInt16();
                    ushort time = reader.ReadUInt16();
                    ushort loopOffset = reader.ReadUInt16();
                    uint nonLoopLength = reader.ReadUInt32();

                    int dataSize = (loopOffset + (int)nonLoopLength) * 4;
                    byte[] audioData = reader.ReadBytes(dataSize);

                    string outputFile = Path.Combine(outputDir, $"{i:X2}.swav");
                    using (FileStream outFs = new FileStream(outputFile, FileMode.Create))
                    using (BinaryWriter writer = new BinaryWriter(outFs))
                    {
                        writer.Write(new byte[] { 0x53, 0x57, 0x41, 0x56, 0xFF, 0xFE, 0x00, 0x01 });
                        writer.Write(dataSize + 0x10 + 0x08 + 12);
                        writer.Write((ushort)0x10);
                        writer.Write((ushort)0x01);
                        writer.Write(new byte[] { 0x44, 0x41, 0x54, 0x41 });
                        writer.Write(dataSize + 0x08 + 12);
                        writer.Write(waveType);
                        writer.Write(loopFlag);
                        writer.Write(sampleRate);
                        writer.Write(time);
                        writer.Write(loopOffset);
                        writer.Write(nonLoopLength);
                        writer.Write(audioData);
                    }

                    OnFileExtracted(outputFile);
                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(outputFile)}");
                    extractedCount++;
                    fs.Seek(currentPos, SeekOrigin.Begin);
                }

                return extractedCount;
            }
        }

        private int FindSignature(byte[] data, byte[] signature, int startIndex)
        {
            for (int i = startIndex; i <= data.Length - signature.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (data[i + j] != signature[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            string dir = Path.GetDirectoryName(filePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(filePath);
            string ext = Path.GetExtension(filePath);
            int counter = 1;

            while (true)
            {
                string newPath = Path.Combine(dir, $"{name}_{counter}{ext}");
                if (!File.Exists(newPath))
                    return newPath;
                counter++;
            }
        }
    }
}