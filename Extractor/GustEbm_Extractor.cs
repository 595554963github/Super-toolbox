using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class GustEbm_Extractor : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static GustEbm_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.gust_ebm.exe", "gust_ebm.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var ebmFiles = Directory.GetFiles(directoryPath, "*.ebm", SearchOption.AllDirectories);
            if (ebmFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.ebm文件");
                OnExtractionFailed("未找到.ebm文件");
                return;
            }

            TotalFilesToExtract = ebmFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{ebmFiles.Length}个EBM文件");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var ebmFilePath in ebmFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(ebmFilePath)}");

                        try
                        {
                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"\"{ebmFilePath}\"",
                                    WorkingDirectory = Path.GetDirectoryName(ebmFilePath),
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
                                string outputJsonPath = Path.ChangeExtension(ebmFilePath, ".json");
                                if (File.Exists(outputJsonPath))
                                {
                                    OnFileExtracted(outputJsonPath);
                                    ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(ebmFilePath)}");
                                }
                                else
                                {
                                    ExtractionError?.Invoke(this, $"处理成功但未找到输出文件:{Path.GetFileName(ebmFilePath)}");
                                }
                            }
                            else
                            {
                                ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(ebmFilePath)}失败，退出代码:{process.ExitCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(ebmFilePath)}处理错误: {ex.Message}");
                            OnExtractionFailed($"文件{Path.GetFileName(ebmFilePath)} 处理错误:{ex.Message}");
                        }
                    }
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