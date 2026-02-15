namespace super_toolbox
{
    public class DarExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private int fileCounter = 0;
        private RIFF_RIFX_Sound_Extractor riffExtractor = new RIFF_RIFX_Sound_Extractor();

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

            string sourceFolderName = new DirectoryInfo(directoryPath).Name;
            string parentDirectory = Directory.GetParent(directoryPath)?.FullName ?? directoryPath;
            string rootExtractedFolder = Path.Combine(parentDirectory, sourceFolderName);
            Directory.CreateDirectory(rootExtractedFolder);

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            riffExtractor.ExtractionProgress += (sender, message) =>
            {
                ExtractionProgress?.Invoke(this, message);
            };

            riffExtractor.ExtractionError += (sender, message) =>
            {
                ExtractionError?.Invoke(this, message);
            };

            riffExtractor.FileExtracted += (sender, fileName) =>
            {
                fileCounter++;
                OnFileExtracted(fileName);
            };

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        string fileName = Path.GetFileName(filePath).ToLower();

                        if (fileName == "data.dar")
                        {
                            await ProcessDataDarFileAsync(filePath, rootExtractedFolder, cancellationToken);
                        }
                        else if (fileName == "voice.dar")
                        {
                            string voiceBaseName = Path.GetFileNameWithoutExtension(filePath);
                            string voiceOutputDir = Path.Combine(rootExtractedFolder, voiceBaseName);
                            Directory.CreateDirectory(voiceOutputDir);
                            await riffExtractor.ProcessFileAsync(filePath, voiceOutputDir, cancellationToken);
                        }
                        else
                        {
                            string normalBaseName = Path.GetFileNameWithoutExtension(filePath);
                            string normalOutputDir = Path.Combine(rootExtractedFolder, normalBaseName);
                            Directory.CreateDirectory(normalOutputDir);
                            await riffExtractor.ProcessFileAsync(filePath, normalOutputDir, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,共提取{fileCounter}个音频文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ProcessDataDarFileAsync(string darFilePath, string rootExtractedFolder, CancellationToken cancellationToken)
        {
            ExtractionProgress?.Invoke(this, $"处理data.dar文件:{Path.GetFileName(darFilePath)}");

            string baseName = Path.GetFileNameWithoutExtension(darFilePath);
            string outputDir = Path.Combine(rootExtractedFolder, baseName);
            Directory.CreateDirectory(outputDir);

            int elzmaCount = 0;

            try
            {
                using (var fs = new FileStream(darFilePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    uint nullValue = reader.ReadUInt32();
                    uint part = reader.ReadUInt32();
                    uint files = reader.ReadUInt32();

                    fs.Seek(0x10, SeekOrigin.Begin);

                    ExtractionProgress?.Invoke(this, $"data.dar中共有{files}个文件");

                    for (int i = 0; i < files; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        uint size = reader.ReadUInt32();
                        uint zsize = reader.ReadUInt32();
                        ulong offset = reader.ReadUInt64();
                        byte[] null3 = reader.ReadBytes(0x10);

                        if (zsize == 0)
                        {
                            continue;
                        }
                        else
                        {
                            elzmaCount++;
                            string outputFile = Path.Combine(outputDir, $"{baseName}_{elzmaCount}.elzma");

                            long currentPos = fs.Position;
                            fs.Seek((long)offset, SeekOrigin.Begin);
                            byte[] fileData = reader.ReadBytes((int)zsize);
                            fs.Seek(currentPos, SeekOrigin.Begin);

                            await File.WriteAllBytesAsync(outputFile, fileData, cancellationToken);
                            fileCounter++;
                            OnFileExtracted(outputFile);
                        }

                        if (i % 100 == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"提取进度:{i + 1}/{files}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"elzma文件提取完成,共{elzmaCount}个");
                }

                string tempAudioFile = Path.Combine(Path.GetTempPath(), $"{baseName}.bin");
                try
                {
                    using (var fs = new FileStream(darFilePath, FileMode.Open, FileAccess.Read))
                    {
                        fs.Seek(0, SeekOrigin.Begin);
                        byte[] fullContent = new byte[fs.Length];
                        await fs.ReadAsync(fullContent, 0, fullContent.Length, cancellationToken);

                        using (var tempFs = new FileStream(tempAudioFile, FileMode.Create, FileAccess.Write))
                        {
                            int position = 0;
                            while (position < fullContent.Length - 8)
                            {
                                if (fullContent[position] == 0x52 && fullContent[position + 1] == 0x49 &&
                                    fullContent[position + 2] == 0x46 && fullContent[position + 3] == 0x46)
                                {
                                    if (position + 8 <= fullContent.Length)
                                    {
                                        int fileSize = BitConverter.ToInt32(fullContent, position + 4);
                                        long totalBlockSize = fileSize + 8;
                                        if (position + totalBlockSize <= fullContent.Length)
                                        {
                                            await tempFs.WriteAsync(fullContent, position, (int)totalBlockSize, cancellationToken);
                                            position += (int)totalBlockSize;
                                            continue;
                                        }
                                    }
                                }
                                position++;
                            }
                        }
                    }

                    if (new FileInfo(tempAudioFile).Length > 0)
                    {
                        await riffExtractor.ProcessFileAsync(tempAudioFile, outputDir, cancellationToken);
                    }
                }
                finally
                {
                    if (File.Exists(tempAudioFile))
                    {
                        File.Delete(tempAudioFile);
                    }
                }

                ExtractionProgress?.Invoke(this, $"data.dar处理完成:{elzmaCount}个elzma文件");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理data.dar时出错:{ex.Message}");
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
