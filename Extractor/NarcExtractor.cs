using Narchive.Formats;

namespace super_toolbox
{
    public class NarcExtractor : BaseExtractor
    {
        private string _directoryPath;
        private int _processedFiles = 0;

        public NarcExtractor()
        {
            _directoryPath = string.Empty;
        }
        public NarcExtractor(string directoryPath)
        {
            _directoryPath = directoryPath;
        }
        public string DirectoryPath
        {
            get => _directoryPath;
            set => _directoryPath = value;
        }
        public override void Extract(string directoryPath)
        {
            _directoryPath = directoryPath;
            ExtractAsync(directoryPath).Wait();
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(directoryPath))
                    {
                        OnExtractionFailed("未设置目录路径");
                        return;
                    }
                    if (!Directory.Exists(directoryPath))
                    {
                        OnExtractionFailed($"目录不存在: {directoryPath}");
                        return;
                    }
                    var narcFiles = Directory.GetFiles(directoryPath, "*.narc", SearchOption.AllDirectories);
                    TotalFilesToExtract = narcFiles.Length;
                    if (narcFiles.Length == 0)
                    {
                        OnExtractionFailed("未找到任何.narc文件");
                        return;
                    }
                    OnProgress($"开始处理{narcFiles.Length}个NARC文件");
                    foreach (var narcFile in narcFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            _processedFiles++;
                            OnProgress($"正在处理{_processedFiles}/{narcFiles.Length}: {Path.GetFileName(narcFile)}");

                            string? fileDirectory = Path.GetDirectoryName(narcFile);
                            if (string.IsNullOrEmpty(fileDirectory))
                            {
                                fileDirectory = directoryPath;
                            }
                            string outputDir = Path.Combine(fileDirectory, Path.GetFileNameWithoutExtension(narcFile));
                            Directory.CreateDirectory(outputDir);
                            NarcArchive.Extract(narcFile, outputDir, false);
                            var extractedFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
                            foreach (var file in extractedFiles)
                            {
                                var relativePath = Path.GetRelativePath(outputDir, file);
                                string fullRelativePath = Path.Combine(Path.GetFileNameWithoutExtension(narcFile), relativePath);
                                OnFileExtracted(fullRelativePath);
                                OnProgress($"已提取:{fullRelativePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"提取失败{Path.GetFileName(narcFile)}: {ex.Message}");
                        }
                    }
                    OnExtractionCompleted();
                }
                catch (OperationCanceledException)
                {
                    OnExtractionFailed("操作已取消");
                }
                catch (Exception ex)
                {
                    OnExtractionFailed($"提取失败:{ex.Message}");
                }
            }, cancellationToken);
        }
        private void OnProgress(string message)
        {
        }
    }
}