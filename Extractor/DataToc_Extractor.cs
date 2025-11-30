namespace super_toolbox
{
    public class DataToc_Extractor : BaseExtractor
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
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var dataFiles = Directory.GetFiles(directoryPath, "*.data", SearchOption.AllDirectories)
                .Where(file => !file.Contains("_extracted", StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = dataFiles.Count;

            if (dataFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"在目录{directoryPath}中未找到.data文件");
                OnExtractionFailed($"在目录{directoryPath}中未找到.data文件");
                return;
            }

            int processedCount = 0;
            int successCount = 0;
            int totalExtractedFiles = 0;

            foreach (var dataFile in dataFiles)
            {
                try
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    processedCount++;
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{dataFiles.Count}):{Path.GetFileName(dataFile)}");

                    string tocFile = FindTocFile(dataFile);
                    if (string.IsNullOrEmpty(tocFile))
                    {
                        ExtractionError?.Invoke(this, $"未找到{Path.GetFileName(dataFile)}对应的.toc文件");
                        continue;
                    }

                    int extractedCount = await UnpackDataFile(dataFile, tocFile, cancellationToken);
                    if (extractedCount > 0)
                    {
                        successCount++;
                        totalExtractedFiles += extractedCount;
                        TotalFilesToExtract = totalExtractedFiles;
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
                    ExtractionError?.Invoke(this, $"处理文件 {Path.GetFileName(dataFile)}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件 {Path.GetFileName(dataFile)}时出错:{ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"处理完成，成功解包{successCount}/{dataFiles.Count}个.data文件，共提取出{totalExtractedFiles}个文件");
            OnExtractionCompleted();
        }

        private string FindTocFile(string dataFilePath)
        {
            string dataDir = Path.GetDirectoryName(dataFilePath) ?? string.Empty;
            string dataFileName = Path.GetFileNameWithoutExtension(dataFilePath);

            string[] possibleTocFiles = {
                Path.Combine(dataDir, dataFileName + ".toc")
            };

            foreach (string tocFile in possibleTocFiles)
            {
                if (File.Exists(tocFile))
                {
                    return tocFile;
                }
            }

            return string.Empty;
        }

        private async Task<int> UnpackDataFile(string dataFilePath, string tocFilePath, CancellationToken cancellationToken)
        {
            try
            {
                string dataFileName = Path.GetFileNameWithoutExtension(dataFilePath);
                string dataDir = Path.GetDirectoryName(dataFilePath) ?? string.Empty;
                string extractDir = Path.Combine(dataDir, dataFileName + "_extracted");
                if (!Directory.Exists(extractDir))
                {
                    Directory.CreateDirectory(extractDir);
                }

                ExtractionProgress?.Invoke(this, $"正在解包:{Path.GetFileName(dataFilePath)}");

                var unpacker = new Shadowrun.Unpacker();

                var originalOutput = Console.Out;
                using (var writer = new StringWriter())
                {
                    Console.SetOut(writer);

                    try
                    {
                        await Task.Run(() =>
                        {
                            unpacker.Unpack(dataFilePath, tocFilePath);
                        }, cancellationToken);
                        string output = writer.ToString();
                        if (!string.IsNullOrEmpty(output))
                        {
                            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    ExtractionProgress?.Invoke(this, line.Trim());
                                }
                            }
                        }
                    }
                    finally
                    {
                        Console.SetOut(originalOutput);
                    }
                }
                string defaultExtractDir = Path.Combine(Directory.GetCurrentDirectory(), "extracted_files");
                if (Directory.Exists(defaultExtractDir))
                {
                    int extractedCount = MoveExtractedFiles(defaultExtractDir, extractDir);
                    ExtractionProgress?.Invoke(this, $"成功解包到:{extractDir}，提取出{extractedCount}个文件");
                    return extractedCount;
                }
                else
                {
                    ExtractionError?.Invoke(this, $"解包失败，未找到输出目录:{defaultExtractDir}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包文件{Path.GetFileName(dataFilePath)}时出错:{ex.Message}");
                return 0;
            }
        }

        private int MoveExtractedFiles(string sourceDir, string targetDir)
        {
            int fileCount = 0;
            try
            {
                if (!Directory.Exists(sourceDir))
                    return 0;

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);
                var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                fileCount = allFiles.Length;

                foreach (string file in allFiles)
                {
                    string relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
                    string targetFile = Path.Combine(targetDir, relativePath);

                    string? targetFileDir = Path.GetDirectoryName(targetFile);
                    if (!string.IsNullOrEmpty(targetFileDir) && !Directory.Exists(targetFileDir))
                        Directory.CreateDirectory(targetFileDir);

                    File.Move(file, targetFile, true);
                    OnFileExtracted(targetFile);
                }

                if (Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories).Length == 0)
                {
                    Directory.Delete(sourceDir, true);
                }

                return fileCount;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"移动提取文件时出错:{ex.Message}");
                return fileCount;
            }
        }
    }
}