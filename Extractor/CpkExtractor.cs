using CriFsV2Lib;
using CriFsV2Lib.Definitions.Interfaces;
using CriFsV2Lib.Definitions.Structs;

namespace super_toolbox
{
    public class CpkExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误: {directoryPath} 不是有效的目录");
                OnExtractionFailed($"错误: {directoryPath} 不是有效的目录");
                return;
            }

            string extractedRootDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedRootDir);

            var cpkFiles = Directory.GetFiles(directoryPath, "*.cpk", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedRootDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = cpkFiles.Count;
            ExtractionStarted?.Invoke(this, $"开始处理{cpkFiles.Count}个CPK文件");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var cpkFilePath in cpkFiles)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string cpkFileName = Path.GetFileNameWithoutExtension(cpkFilePath);
                            string cpkExtractDir = Path.Combine(extractedRootDir, cpkFileName);
                            Directory.CreateDirectory(cpkExtractDir);

                            ExtractionProgress?.Invoke(this, $"正在处理: {Path.GetFileName(cpkFilePath)}");

                            using (var fileStream = new FileStream(cpkFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                var cpk = CriFsLib.Instance.CreateCpkReader(fileStream, true);
                                var files = cpk.GetFiles();

                                int totalFiles = files.Length;
                                int processedFiles = 0;

                                ExtractionProgress?.Invoke(this, $"CPK内包含{totalFiles}个文件");
                                foreach (var file in files)
                                {
                                    string relativePath = !string.IsNullOrEmpty(file.Directory)
                                        ? Path.Combine(file.Directory, file.FileName)
                                        : file.FileName;
                                    ExtractionProgress?.Invoke(this, $"发现文件: {relativePath}");
                                }
                                using (var extractor = CriFsLib.Instance.CreateBatchExtractor<CpkFileExtractorItem>(cpkFilePath))
                                {
                                    foreach (var file in files)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();

                                        string relativePath = !string.IsNullOrEmpty(file.Directory)
                                            ? Path.Combine(file.Directory, file.FileName)
                                            : file.FileName;

                                        string outputPath = Path.Combine(cpkExtractDir, relativePath);
                                        string? outputDir = Path.GetDirectoryName(outputPath);
                                        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                                            Directory.CreateDirectory(outputDir);

                                        extractor.QueueItem(new CpkFileExtractorItem(outputPath, file));
                                    }
                                    ExtractionProgress?.Invoke(this, "开始提取文件...");

                                    extractor.WaitForCompletion();
                                    foreach (var file in files)
                                    {
                                        string relativePath = !string.IsNullOrEmpty(file.Directory)
                                            ? Path.Combine(file.Directory, file.FileName)
                                            : file.FileName;

                                        string outputPath = Path.Combine(cpkExtractDir, relativePath);

                                        if (File.Exists(outputPath))
                                        {
                                            processedFiles++;
                                            ExtractionProgress?.Invoke(this, $"已提取: {relativePath} ({processedFiles}/{totalFiles})");
                                            OnFileExtracted(outputPath);
                                        }
                                        else
                                        {
                                            ExtractionError?.Invoke(this, $"提取失败: {relativePath}");
                                        }
                                    }
                                }

                                ExtractionProgress?.Invoke(this, $"完成处理: {Path.GetFileName(cpkFilePath)} -> {processedFiles}/{totalFiles} 个文件");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理 {Path.GetFileName(cpkFilePath)} 时出错: {ex.Message}");
                            OnExtractionFailed($"处理 {Path.GetFileName(cpkFilePath)} 时出错: {ex.Message}");
                        }
                    }
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败: {ex.Message}");
                OnExtractionFailed($"提取失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private class CpkFileExtractorItem : IBatchFileExtractorItem
        {
            public string FullPath { get; }
            public CpkFile File { get; }

            public CpkFileExtractorItem(string fullPath, CpkFile file)
            {
                FullPath = fullPath;
                File = file;
            }
        }
    }
}