namespace super_toolbox
{
    public class DW4_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private int fileCounter = 0;
        private const string TXTH_CONFIG = @"codec = PSX
        channels = 1
        sample_rate = 22050
        start_offset = 0x00
        num_samples = data_size";

        private Tm2_Extractor tm2Extractor = new Tm2_Extractor();

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

            tm2Extractor.ExtractionProgress += (sender, message) =>
            {
                ExtractionProgress?.Invoke(this, message);
            };

            tm2Extractor.ExtractionError += (sender, message) =>
            {
                ExtractionError?.Invoke(this, message);
            };

            tm2Extractor.FileExtracted += (sender, fileName) =>
            {
                fileCounter++;
                OnFileExtracted(fileName);
            };

            string[] idxFiles = Directory.GetFiles(directoryPath, "*.IDX", SearchOption.TopDirectoryOnly);

            if (idxFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "目录下未找到任何.IDX文件");
                OnExtractionFailed("目录下未找到任何.IDX文件");
                return;
            }

            foreach (string idxFile in idxFiles)
            {
                string binFileName = Path.GetFileNameWithoutExtension(idxFile) + ".BIN";
                string binFile = Path.Combine(directoryPath, binFileName);
                string baseName = Path.GetFileNameWithoutExtension(binFile);

                ExtractionProgress?.Invoke(this, $"开始处理{Path.GetFileName(idxFile)}对应的{binFileName}");

                if (!File.Exists(binFile))
                {
                    ExtractionError?.Invoke(this, $"{binFileName}不存在,跳过该IDX文件的处理");
                    continue;
                }

                try
                {
                    if (baseName.StartsWith("LINKDATA", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractionProgress?.Invoke(this, $"检测到LINKDATA文件:{binFile},调用TM2提取器处理");
                        await tm2Extractor.ExtractAsync(binFile, cancellationToken);
                    }
                    else
                    {
                        await ExtractSingleFile(idxFile, binFile, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理{binFileName}时出错:{ex.Message},继续处理下一个文件");
                }
            }

            ExtractionProgress?.Invoke(this, $"所有IDX/BIN文件处理完成");
            OnExtractionCompleted();
        }

        private async Task ExtractSingleFile(string idxFile, string binFile, CancellationToken cancellationToken)
        {
            string baseName = Path.GetFileNameWithoutExtension(binFile);
            string outputDirectory = Path.Combine(Path.GetDirectoryName(idxFile)!, baseName);

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                ExtractionProgress?.Invoke(this, $"创建输出文件夹:{outputDirectory}");
            }

            using (var idxStream = new FileStream(idxFile, FileMode.Open, FileAccess.Read))
            using (var idxReader = new BinaryReader(idxStream))
            using (var binStream = new FileStream(binFile, FileMode.Open, FileAccess.Read))
            {
                byte[] header = idxReader.ReadBytes(16);
                string headerString = System.Text.Encoding.ASCII.GetString(header, 0, 4);
                uint files;

                if (headerString == "LINK")
                {
                    ExtractionProgress?.Invoke(this, $"检测到LINK标识的文件头:{baseName}");
                    long idxFileSize = idxStream.Length;
                    files = (uint)((idxFileSize - 16) / 16);
                    ExtractionProgress?.Invoke(this, $"通过文件大小计算出文件数量:{files}");

                    idxStream.Seek(16, SeekOrigin.Begin);
                }
                else
                {
                    files = BitConverter.ToUInt32(header, 0);

                    if (files == 0 || files > 100000)
                    {
                        ExtractionError?.Invoke(this, $"{baseName}.BIN文件数量异常:{files},尝试直接解析");
                        long idxFileSize = idxStream.Length;
                        files = (uint)(idxFileSize / 16);
                        ExtractionProgress?.Invoke(this, $"通过文件大小计算出文件数量:{files}");
                        idxStream.Seek(0, SeekOrigin.Begin);
                    }
                }

                if (files == 0 || files > 100000)
                {
                    ExtractionError?.Invoke(this, $"{baseName}.BIN文件数量异常:{files},跳过");
                    return;
                }

                TotalFilesToExtract = (int)files;
                ExtractionProgress?.Invoke(this, $"{baseName}.BIN中共有{files}个文件");

                List<uint> offsets = new List<uint>();
                List<uint> sizes = new List<uint>();

                bool isVoiceOrSe = baseName == "LINKVOI" || baseName == "LINKSE";
                bool isMovie = baseName == "LINKMOV";
                bool isBgm = baseName == "LINKBGM";

                for (int i = 0; i < files; i++)
                {
                    if (idxStream.Position + 16 > idxStream.Length)
                    {
                        ExtractionError?.Invoke(this, $"{Path.GetFileName(idxFile)}索引文件读取超出边界,停止解析该文件");
                        break;
                    }

                    uint offset = idxReader.ReadUInt32();
                    uint xsize = idxReader.ReadUInt32();
                    uint size = idxReader.ReadUInt32();
                    uint zero = idxReader.ReadUInt32();

                    offsets.Add(offset);
                    sizes.Add(size);
                }

                int localFileCounter = 0;

                for (int i = 0; i < offsets.Count; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    uint offset = offsets[i];
                    uint size = sizes[i];
                    long fileOffset = offset * 0x800;

                    if (fileOffset >= binStream.Length)
                    {
                        ExtractionProgress?.Invoke(this, $"{baseName}.BIN文件{i}偏移0x{fileOffset:X}超出范围,跳过");
                        continue;
                    }

                    long bytesToRead = size;
                    if (fileOffset + bytesToRead > binStream.Length)
                    {
                        bytesToRead = binStream.Length - fileOffset;
                        ExtractionProgress?.Invoke(this, $"{baseName}.BIN文件{i}大小超出范围,截取到{bytesToRead}字节");
                    }

                    if (bytesToRead <= 0)
                    {
                        continue;
                    }

                    if ((i + 1) % 500 == 0 || i == 0 || i + 1 == offsets.Count)
                    {
                        ExtractionProgress?.Invoke(this, $"正在提取{baseName}.BIN文件{i + 1}/{offsets.Count}偏移:0x{fileOffset:X}大小:{bytesToRead}字节");
                    }

                    binStream.Seek(fileOffset, SeekOrigin.Begin);
                    byte[] fileData = new byte[bytesToRead];
                    int bytesRead = 0;
                    while (bytesRead < bytesToRead)
                    {
                        int read = await binStream.ReadAsync(fileData, bytesRead, (int)(bytesToRead - bytesRead), cancellationToken);
                        if (read == 0) break;
                        bytesRead += read;
                    }

                    if (bytesRead > 0)
                    {
                        string extension;

                        if (isMovie)
                        {
                            extension = ".pss";
                        }
                        else if (isVoiceOrSe)
                        {
                            extension = ".pcm";
                        }
                        else if (isBgm)
                        {
                            extension = ".mic";
                        }
                        else
                        {
                            extension = ".bin";
                        }

                        string outputFile = Path.Combine(outputDirectory, $"{baseName}_{localFileCounter + 1}{extension}");
                        outputFile = MakeUniqueFilename(outputFile);

                        await File.WriteAllBytesAsync(outputFile, fileData, cancellationToken);

                        if (isVoiceOrSe)
                        {
                            await CreateTxthConfigFileAsync(outputFile, cancellationToken);
                        }

                        localFileCounter++;
                        fileCounter++;
                        OnFileExtracted(outputFile);
                    }

                    if ((i + 1) % 100 == 0 || i + 1 == offsets.Count)
                    {
                        ExtractionProgress?.Invoke(this, $"{baseName}.BIN提取进度:{i + 1}/{offsets.Count},已提取{localFileCounter}个文件");
                    }
                }

                ExtractionProgress?.Invoke(this, $"{baseName}.BIN处理完成,本次提取{localFileCounter}个文件");
            }
        }

        private async Task CreateTxthConfigFileAsync(string pcmFilePath, CancellationToken cancellationToken)
        {
            try
            {
                string txthFilePath = pcmFilePath + ".txth";

                if (!File.Exists(txthFilePath))
                {
                    await File.WriteAllTextAsync(txthFilePath, TXTH_CONFIG, cancellationToken);
                    ExtractionProgress?.Invoke(this, $"已创建配置文件:{Path.GetFileName(txthFilePath)}");
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"创建.txth配置文件时出错:{ex.Message}");
            }
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

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
