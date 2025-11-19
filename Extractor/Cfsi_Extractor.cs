using ZE_CFSI_Lib;

namespace super_toolbox
{
    public class Cfsi_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var cfsiFiles = Directory.GetFiles(directoryPath, "*.cfsi", SearchOption.AllDirectories);

            if (cfsiFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "在指定目录中未找到.cfsi文件");
                OnExtractionFailed("在指定目录中未找到.cfsi文件");
                return;
            }

            TotalFilesToExtract = cfsiFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始提取{cfsiFiles.Length}个CFSI文件");

            await Task.Run(() =>
            {
                foreach (var cfsiFile in cfsiFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    CFSI_Lib? cfsiLib = null;
                    try
                    {
                        ExtractionProgress?.Invoke(this, $"正在处理: {Path.GetFileName(cfsiFile)}");

                        string outputDir = Path.Combine(Path.GetDirectoryName(cfsiFile) ?? directoryPath,
                              Path.GetFileNameWithoutExtension(cfsiFile));

                        cfsiLib = new CFSI_Lib(cfsiFile);

                        foreach (var fileEntry in cfsiLib.Files)
                        {
                            ThrowIfCancellationRequested(cancellationToken);

                            string outputPath = Path.Combine(outputDir, fileEntry.Path);
                            cfsiLib.ExtractFile(fileEntry, outputDir);

                            OnFileExtracted(outputPath);
                            ExtractionProgress?.Invoke(this, $"已提取: {fileEntry.Name}");
                        }

                        ExtractionProgress?.Invoke(this, $"成功提取{Path.GetFileName(cfsiFile)} -> {cfsiLib.Files.Count}个文件");
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"提取 {Path.GetFileName(cfsiFile)}时出错:{ex.Message}");
                        OnExtractionFailed($"提取{Path.GetFileName(cfsiFile)}时出错:{ex.Message}");
                    }
                    finally
                    {
                        cfsiLib?.Dispose();
                    }
                }

                ExtractionProgress?.Invoke(this, $"CFSI提取完成，共从{cfsiFiles.Length}个档案中提取出{ExtractedFileCount}个文件");
                OnExtractionCompleted();
            }, cancellationToken);
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}