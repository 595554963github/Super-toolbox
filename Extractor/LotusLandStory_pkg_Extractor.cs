namespace super_toolbox
{
    public class LotusLandStory_pkg_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private const int ENTRY_SIZE = 80;
        private int fileCounter = 0;

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

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.pkg", SearchOption.AllDirectories).ToList();

            TotalFilesToExtract = filePaths.Count;
            int processedFiles = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)} ({processedFiles}/{TotalFilesToExtract})");

                    try
                    {
                        await ProcessPkgFileAsync(filePath, cancellationToken);
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

                ExtractionProgress?.Invoke(this, $"处理完成，共提取{fileCounter}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
        }

        private async Task ProcessPkgFileAsync(string pkgFilePath, CancellationToken cancellationToken)
        {
            ExtractionProgress?.Invoke(this, $"处理PKG文件:{Path.GetFileName(pkgFilePath)}");

            byte[] content = await File.ReadAllBytesAsync(pkgFilePath, cancellationToken);

            if (content.Length < 8)
            {
                ExtractionError?.Invoke(this, $"文件太小:{pkgFilePath}");
                return;
            }

            int fileCount = BitConverter.ToInt32(content, 0x04);
            ExtractionProgress?.Invoke(this, $"文件数量:{fileCount}");

            List<PkgEntry> entries = new List<PkgEntry>();
            int currentPosition = 0x08;

            for (int i = 0; i < fileCount; i++)
            {
                if (currentPosition + ENTRY_SIZE > content.Length)
                {
                    break;
                }

                string fileName = "";
                for (int j = 0; j < 64; j++)
                {
                    byte b = content[currentPosition + j];
                    if (b == 0) break;
                    fileName += (char)b;
                }

                int fileSize = BitConverter.ToInt32(content, currentPosition + 68);
                int fileOffset = BitConverter.ToInt32(content, currentPosition + 72);

                if (!string.IsNullOrWhiteSpace(fileName) && fileSize > 0 && fileOffset > 0 &&
                    fileOffset < content.Length && fileOffset + fileSize <= content.Length)
                {
                    entries.Add(new PkgEntry
                    {
                        FileName = fileName,
                        Size = fileSize,
                        Offset = fileOffset
                    });
                }

                currentPosition += ENTRY_SIZE;
            }

            if (entries.Count == 0)
            {
                ExtractionError?.Invoke(this, $"未找到有效的文件条目:{pkgFilePath}");
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(pkgFilePath);
            string outputDir = Path.Combine(Path.GetDirectoryName(pkgFilePath) ?? "", $"{baseName}");
            Directory.CreateDirectory(outputDir);

            await ExtractFilesFromDataSectionAsync(content, entries, outputDir, cancellationToken);
        }

        private async Task ExtractFilesFromDataSectionAsync(byte[] content, List<PkgEntry> entries, string outputDir, CancellationToken cancellationToken)
        {
            int extractedCount = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                ThrowIfCancellationRequested(cancellationToken);

                var entry = entries[i];

                try
                {
                    string outputFilePath = Path.Combine(outputDir, entry.FileName);

                    string fileDir = Path.GetDirectoryName(outputFilePath) ?? outputDir;
                    if (!Directory.Exists(fileDir))
                    {
                        Directory.CreateDirectory(fileDir);
                    }

                    byte[] fileData = new byte[entry.Size];
                    Array.Copy(content, entry.Offset, fileData, 0, entry.Size);

                    await File.WriteAllBytesAsync(outputFilePath, fileData, cancellationToken);

                    extractedCount++;
                    fileCounter++;
                    OnFileExtracted(outputFilePath);
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"保存文件{entry.FileName}时出错:{ex.Message}");
                }
            }
        }

        private class PkgEntry
        {
            public string FileName { get; set; } = "";
            public int Size { get; set; }
            public int Offset { get; set; }
        }
    }
}