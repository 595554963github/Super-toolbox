using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class GustGmpk_Extractor : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static GustGmpk_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.gust_gmpk.exe", "gust_gmpk.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }
            var gmpkFiles = Directory.GetFiles(directoryPath, "*.gmpk", SearchOption.AllDirectories);
            if (gmpkFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.gmpk文件");
                OnExtractionFailed("未找到.gmpk文件");
                return;
            }
            TotalFilesToExtract = gmpkFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{gmpkFiles.Length}个GMPK文件");
            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    int totalExtractedFiles = 0;
                    foreach (var gmpkFilePath in gmpkFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedCount++;
                        if (processedCount % 10 == 1 || processedCount == gmpkFiles.Length)
                        {
                            ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(gmpkFilePath)} ({processedCount}/{gmpkFiles.Length})");
                        }
                        try
                        {
                            string? parentDir = Path.GetDirectoryName(gmpkFilePath);
                            if (string.IsNullOrEmpty(parentDir))
                            {
                                ExtractionError?.Invoke(this, $"无法获取文件目录:{gmpkFilePath}");
                                continue;
                            }
                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"\"{gmpkFilePath}\"",
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
                                if (!string.IsNullOrEmpty(e.Data))
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
                                ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(gmpkFilePath)}失败，退出代码:{process.ExitCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(gmpkFilePath)}处理错误:{ex.Message}");
                        }
                    }
                    var allExistingFiles = new HashSet<string>(
                        Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                    );
                    var newFiles = allExistingFiles
                        .Except(gmpkFiles)
                        .Where(f => !f.EndsWith(".gmpk", StringComparison.OrdinalIgnoreCase))
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