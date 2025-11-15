using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class GustG1t_Extractor : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static GustG1t_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.gust_g1t.exe", "gust_g1t.exe");
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }
            var g1tFiles = Directory.GetFiles(directoryPath, "*.g1t", SearchOption.AllDirectories);
            if (g1tFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.g1t文件");
                OnExtractionFailed("未找到.g1t文件");
                return;
            }
            TotalFilesToExtract = g1tFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{g1tFiles.Length}个G1T文件");
            try
            {
                await Task.Run(() =>
                {
                    foreach (var g1tFilePath in g1tFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(g1tFilePath)}");

                        try
                        {
                            string? parentDir = Path.GetDirectoryName(g1tFilePath);
                            if (string.IsNullOrEmpty(parentDir))
                            {
                                ExtractionError?.Invoke(this, $"无法获取文件目录:{g1tFilePath}");
                                OnExtractionFailed($"无法获取文件目录:{g1tFilePath}");
                                continue;
                            }
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(g1tFilePath);
                            string outputDir = Path.Combine(parentDir, fileNameWithoutExt);
                            if (!Directory.Exists(outputDir))
                            {
                                Directory.CreateDirectory(outputDir);
                            }
                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"\"{g1tFilePath}\"",
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
                                var ddsFiles = Directory.GetFiles(outputDir, "*.dds", SearchOption.AllDirectories);
                                foreach (var ddsFile in ddsFiles)
                                {
                                    OnFileExtracted(ddsFile);
                                }
                                ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(g1tFilePath)} -> 生成{ddsFiles.Length}个DDS文件");
                            }
                            else
                            {
                                ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(g1tFilePath)}失败，退出代码:{process.ExitCode}");
                                OnExtractionFailed($"处理文件{Path.GetFileName(g1tFilePath)}失败，退出代码:{process.ExitCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(g1tFilePath)}处理错误:{ex.Message}");
                            OnExtractionFailed($"文件{Path.GetFileName(g1tFilePath)} 处理错误:{ex.Message}");
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