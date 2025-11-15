using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class GustPak_Extractor : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static GustPak_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.gust_pak.exe", "gust_pak.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var pakFiles = Directory.GetFiles(directoryPath, "*.pak", SearchOption.AllDirectories);
            if (pakFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.pak文件");
                OnExtractionFailed("未找到.pak文件");
                return;
            }

            TotalFilesToExtract = pakFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{pakFiles.Length}个PAK文件");

            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    int totalExtractedFiles = 0;

                    foreach (var pakFilePath in pakFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        processedCount++;
                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(pakFilePath)} ({processedCount}/{pakFiles.Length})");

                        try
                        {
                            string? parentDir = Path.GetDirectoryName(pakFilePath);
                            if (string.IsNullOrEmpty(parentDir))
                            {
                                ExtractionError?.Invoke(this, $"无法获取文件目录:{pakFilePath}");
                                continue;
                            }
                            var filesBefore = new HashSet<string>(
                                Directory.GetFiles(parentDir, "*.*", SearchOption.AllDirectories)
                                    .Where(f => !f.EndsWith(".pak"))
                            );

                            using (var process = new Process())
                            {
                                process.StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"\"{pakFilePath}\"",
                                    WorkingDirectory = parentDir,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    StandardOutputEncoding = Encoding.UTF8,
                                    StandardErrorEncoding = Encoding.UTF8
                                };

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

                                process.Start();
                                process.BeginOutputReadLine();
                                process.BeginErrorReadLine();
                                process.WaitForExit();

                                if (process.ExitCode == 0)
                                {
                                    var filesAfter = new HashSet<string>(
                                        Directory.GetFiles(parentDir, "*.*", SearchOption.AllDirectories)
                                            .Where(f => !f.EndsWith(".pak"))
                                    );

                                    var newFiles = filesAfter.Except(filesBefore).ToArray();

                                    foreach (var newFile in newFiles)
                                    {
                                        OnFileExtracted(newFile);
                                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(newFile)}");
                                    }

                                    totalExtractedFiles += newFiles.Length;
                                    ExtractionProgress?.Invoke(this, $"提取成功:{Path.GetFileName(pakFilePath)} → 已提取{newFiles.Length}个文件");
                                }
                                else
                                {
                                    ExtractionError?.Invoke(this, $"失败:{Path.GetFileName(pakFilePath)}(代码:{process.ExitCode})");
                                }
                            } 
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"错误:{Path.GetFileName(pakFilePath)} - {ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"全部完成! 处理了{pakFiles.Length}个PAK文件，共提取{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}