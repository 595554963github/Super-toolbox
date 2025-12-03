using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class XenobladeBdat_Extractor : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static XenobladeBdat_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.bdat-toolset.exe", "bdat-toolset.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var bdatFiles = Directory.GetFiles(directoryPath, "*.bdat", SearchOption.AllDirectories);

            if (bdatFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.bdat文件");
                OnExtractionFailed("未找到.bdat文件");
                return;
            }

            TotalFilesToExtract = bdatFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{bdatFiles.Length}个BDAT文件");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var bdatFilePath in bdatFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        string fileName = Path.GetFileName(bdatFilePath);
                        ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                        try
                        {
                            string outputDir = Path.Combine(Path.GetDirectoryName(bdatFilePath) ?? directoryPath,
                                                          Path.GetFileNameWithoutExtension(bdatFilePath));
                            if (Directory.Exists(outputDir))
                            {
                                ExtractionProgress?.Invoke(this, $"跳过{fileName},输出目录已存在");
                                continue;
                            }
                            Directory.CreateDirectory(outputDir);
                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"extract \"{bdatFilePath}\" -o \"{outputDir}\" -f json --pretty",
                                    WorkingDirectory = Path.GetDirectoryName(bdatFilePath),
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
                                    ExtractionProgress?.Invoke(this, $"[{fileName}] {e.Data}");
                                }
                            };
                            process.ErrorDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                {
                                    ExtractionError?.Invoke(this, $"[{fileName}] 错误:{e.Data}");
                                }
                            };
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                            process.WaitForExit();
                            if (process.ExitCode == 0)
                            {
                                var extractedFiles = Directory.GetFiles(outputDir, "*.json", SearchOption.AllDirectories);
                                if (extractedFiles.Length > 0)
                                {
                                    foreach (var extractedFile in extractedFiles)
                                    {
                                        OnFileExtracted(extractedFile);
                                    }
                                    ExtractionProgress?.Invoke(this, $"完成处理:{fileName},提取了{extractedFiles.Length}个JSON文件");
                                }
                                else
                                {
                                    ExtractionError?.Invoke(this, $"处理成功但未找到JSON文件:{fileName}");
                                    OnExtractionFailed($"处理成功但未找到JSON文件:{fileName}");
                                }
                            }
                            else
                            {
                                ExtractionError?.Invoke(this, $"处理文件{fileName}失败,退出代码:{process.ExitCode}");
                                OnExtractionFailed($"处理文件{fileName}失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{fileName}处理错误:{ex.Message}");
                            OnExtractionFailed($"文件{fileName}处理错误:{ex.Message}");
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