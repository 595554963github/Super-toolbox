using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class GustElixir_Extractor : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        static GustElixir_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.gust_elixir.exe", "gust_elixir.exe");
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:目录不存在或路径为空");
                OnExtractionFailed("错误:目录不存在或路径为空");
                return;
            }
            var elixirFiles = Directory.GetFiles(directoryPath, "*.elixir*", SearchOption.AllDirectories);
            var gzFiles = Directory.GetFiles(directoryPath, "*.gz", SearchOption.AllDirectories);
            var allFiles = elixirFiles.Concat(gzFiles).ToArray();

            if (allFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.elixir或.gz文件");
                OnExtractionFailed("未找到.elixir或.gz文件");
                return;
            }
            TotalFilesToExtract = allFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{allFiles.Length}个文件(.elixir/.gz)");
            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    int totalExtractedFiles = 0;

                    foreach (var filePath in allFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        processedCount++;

                        if (processedCount % 10 == 1 || processedCount == allFiles.Length)
                        {
                            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)} ({processedCount}/{allFiles.Length})");
                        }
                        try
                        {
                            string? parentDir = Path.GetDirectoryName(filePath);
                            if (string.IsNullOrEmpty(parentDir))
                            {
                                ExtractionError?.Invoke(this, $"无法获取文件目录:{filePath}");
                                continue;
                            }
                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"\"{filePath}\"",
                                    WorkingDirectory = parentDir,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    StandardOutputEncoding = Encoding.UTF8,
                                    StandardErrorEncoding = Encoding.UTF8
                                }
                            };
                            process.Start();
                            process.OutputDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data) &&
                                   (e.Data.Contains("ERROR") || e.Data.Contains("error") || e.Data.Contains("完成")))
                                {
                                    ExtractionProgress?.Invoke(this, e.Data);
                                }
                            };
                            process.ErrorDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                {
                                    ExtractionError?.Invoke(this, $"错误:{e.Data}");
                                }
                            };
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                            process.WaitForExit();

                            if (process.ExitCode == 0)
                            {
                            }
                            else
                            {
                                ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(filePath)}失败，退出代码:{process.ExitCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(filePath)}处理错误: {ex.Message}");
                        }
                    }
                    var allExistingFiles = new HashSet<string>(
                        Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                    );
                    var newFiles = allExistingFiles
                        .Except(allFiles)
                        .Where(f => !f.EndsWith(".elixir") && !f.EndsWith(".gz"))
                        .ToArray();
                    totalExtractedFiles = newFiles.Length;
                    foreach (var newFile in newFiles)
                    {
                        OnFileExtracted(newFile);
                    }
                    ExtractionProgress?.Invoke(this, $"提取完成，总共生成{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}