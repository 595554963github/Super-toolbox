using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class Wiiu_h3appExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempExePath;

        static Wiiu_h3appExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "cdecrypt.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.cdecrypt.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的EXE资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹 {directoryPath} 不存在");
                OnExtractionFailed($"源文件夹 {directoryPath} 不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            // 获取处理前的所有文件列表（用于后续对比）
            var originalFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

            // 统计源文件数量（.app、.h3、.tik、.tmd、.cert文件）
            var sourceFiles = originalFiles
                .Where(file => file.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ||
                              file.EndsWith(".h3", StringComparison.OrdinalIgnoreCase) ||
                              file.EndsWith(".tik", StringComparison.OrdinalIgnoreCase) ||
                              file.EndsWith(".tmd", StringComparison.OrdinalIgnoreCase) ||
                              file.EndsWith(".cert", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int totalSourceFiles = sourceFiles.Count;

            if (totalSourceFiles == 0)
            {
                ExtractionError?.Invoke(this, "目录不包含有效的Wii U游戏文件（缺少.app/.h3/.tik/.tmd/.cert文件）");
                OnExtractionFailed("目录不包含有效的Wii U游戏文件（缺少.app/.h3/.tik/.tmd/.cert文件）");
                return;
            }

            ExtractionProgress?.Invoke(this, $"找到 {totalSourceFiles} 个游戏源文件");

            // 实时监控提取的文件
            var extractedFiles = new List<string>();
            var fileWatcher = new FileSystemWatcher
            {
                Path = directoryPath,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
            };

            fileWatcher.Created += (sender, e) =>
            {
                // 排除原始文件和临时文件
                if (!originalFiles.Contains(e.FullPath) &&
                    !e.FullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
                    !e.FullPath.Contains("\\temp\\"))
                {
                    lock (extractedFiles)
                    {
                        if (!extractedFiles.Contains(e.FullPath))
                        {
                            extractedFiles.Add(e.FullPath);
                            // 实时更新状态栏
                            TotalFilesToExtract = extractedFiles.Count;
                            ExtractionProgress?.Invoke(this, $"检测到新文件({extractedFiles.Count}): {Path.GetFileName(e.FullPath)}");
                            OnFileExtracted(e.FullPath);
                        }
                    }
                }
            };

            try
            {
                ExtractionProgress?.Invoke(this, "正在使用 cdecrypt.exe 处理游戏文件...");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"\"{directoryPath}\"",
                        WorkingDirectory = directoryPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                StringBuilder output = new StringBuilder();
                StringBuilder error = new StringBuilder();

                process.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output.AppendLine(e.Data);
                        ExtractionProgress?.Invoke(this, $"cdecrypt: {e.Data}");
                    }
                };
                process.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        error.AppendLine(e.Data);
                        ExtractionError?.Invoke(this, $"cdecrypt错误: {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 等待进程完成，支持取消
                await Task.Run(() =>
                {
                    while (!process.HasExited)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Thread.Sleep(100);
                    }
                }, cancellationToken);

                if (process.ExitCode != 0)
                {
                    ExtractionError?.Invoke(this, $"cdecrypt.exe 处理失败，退出代码: {process.ExitCode}");
                    OnExtractionFailed($"cdecrypt.exe 处理失败，退出代码: {process.ExitCode}");
                    return;
                }

                // 等待所有文件写入完成
                await Task.Delay(1000, cancellationToken);

                // 最终验证：获取处理后的所有文件列表，确保没有遗漏
                var allFilesAfterExtraction = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
                var finalExtractedFiles = allFilesAfterExtraction.Except(originalFiles).ToList();

                // 添加可能遗漏的文件
                lock (extractedFiles)
                {
                    foreach (var file in finalExtractedFiles)
                    {
                        if (!extractedFiles.Contains(file))
                        {
                            extractedFiles.Add(file);
                            ExtractionProgress?.Invoke(this, $"添加遗漏文件: {Path.GetFileName(file)}");
                        }
                    }
                }

                // 正确的统计信息显示
                ExtractionProgress?.Invoke(this, $"处理完成，共处理 {totalSourceFiles} 个游戏源文件，提取出 {extractedFiles.Count} 个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理失败: {ex.Message}");
                OnExtractionFailed($"处理失败: {ex.Message}");
            }
            finally
            {
                fileWatcher.EnableRaisingEvents = false;
                fileWatcher.Dispose();
            }
        }
    }
}