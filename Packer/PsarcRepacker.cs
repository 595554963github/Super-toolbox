using System.Diagnostics;

namespace super_toolbox
{
    public class PsarcRepacker : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;
        private int _processedSourceFiles = 0;
        private int _totalSourceFiles = 0;

        static PsarcRepacker()
        {
            _tempExePath = LoadEmbeddedExe("embedded.psarc.exe", "psarc.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await RepackAsync(directoryPath, cancellationToken);
        }

        public async Task RepackAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            _processedSourceFiles = 0;
            _totalSourceFiles = 0;

            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, "错误:目录不存在");
                OnPackingFailed("错误:目录不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            _totalSourceFiles = allFiles.Length;

            if (_totalSourceFiles == 0)
            {
                PackingError?.Invoke(this, "目录中未找到任何文件");
                OnPackingFailed("目录中未找到任何文件");
                return;
            }

            TotalFilesToPack = _totalSourceFiles;

            PackingStarted?.Invoke(this, $"开始打包PSARC文件");
            PackingProgress?.Invoke(this, "===========================================");
            PackingProgress?.Invoke(this, "• PSARC打包器 - 将目录打包为PSARC文件");
            PackingProgress?.Invoke(this, "• 自动打包目录及其所有子目录中的文件");
            PackingProgress?.Invoke(this, "===========================================");
            PackingProgress?.Invoke(this, $"找到{_totalSourceFiles}个文件:");

            var fileExtensions = allFiles.GroupBy(f => Path.GetExtension(f).ToLower())
                                       .Select(g => new { Extension = g.Key, Count = g.Count() })
                                       .OrderByDescending(x => x.Count);

            foreach (var extGroup in fileExtensions.Take(10))
            {
                PackingProgress?.Invoke(this, $"  - {extGroup.Extension}:{extGroup.Count}个文件");
            }

            if (fileExtensions.Count() > 10)
            {
                PackingProgress?.Invoke(this, $"  - ...及其他{fileExtensions.Count() - 10}种文件类型");
            }

            string parentDir = Path.GetDirectoryName(directoryPath) ?? Directory.GetCurrentDirectory();
            string folderName = Path.GetFileName(directoryPath);
            string expectedOutputFile = Path.Combine(parentDir, folderName + ".psarc");

            PackingProgress?.Invoke(this, $"输入目录:{directoryPath}");
            PackingProgress?.Invoke(this, $"预期输出:{expectedOutputFile}");
            PackingProgress?.Invoke(this, $"工作目录:{parentDir}");

            try
            {
                foreach (var file in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string relativePath = Path.GetRelativePath(directoryPath, file);
                    PackingProgress?.Invoke(this, $"处理源文件:{relativePath}");
                    _processedSourceFiles++;
                    OnFilePacked(file);
                }

                PackingProgress?.Invoke(this, "正在创建PSARC文件...");

                string fullCommand = $"create \"{directoryPath}\"";
                PackingProgress?.Invoke(this, $"执行命令:psarc {fullCommand}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"create \"{directoryPath}\"",
                    WorkingDirectory = parentDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        PackingError?.Invoke(this, "无法启动打包进程");
                        OnPackingFailed("无法启动打包进程");
                        return;
                    }

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync(cancellationToken);

                    if (!string.IsNullOrEmpty(output))
                    {
                        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            PackingProgress?.Invoke(this, line);
                        }
                    }

                    if (!string.IsNullOrEmpty(error))
                    {
                        foreach (string line in error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            PackingError?.Invoke(this, $"错误:{line}");
                        }
                    }

                    if (process.ExitCode != 0)
                    {
                        PackingError?.Invoke(this, $"打包失败:进程退出代码{process.ExitCode}");
                        OnPackingFailed($"打包失败:进程退出代码{process.ExitCode}");
                    }
                    else
                    {
                        if (File.Exists(expectedOutputFile))
                        {
                            FileInfo fileInfo = new FileInfo(expectedOutputFile);
                            PackingProgress?.Invoke(this, $"打包完成:{Path.GetFileName(expectedOutputFile)}({FormatFileSize(fileInfo.Length)})");
                            PackingProgress?.Invoke(this, $"输出路径:{expectedOutputFile}");
                            OnPackingCompleted();
                        }
                        else
                        {
                            var psarcFiles = Directory.GetFiles(parentDir, "*.psarc");
                            if (psarcFiles.Length > 0)
                            {
                                string actualOutputFile = psarcFiles[0];
                                FileInfo fileInfo = new FileInfo(actualOutputFile);
                                PackingProgress?.Invoke(this, $"打包完成:{Path.GetFileName(actualOutputFile)}({FormatFileSize(fileInfo.Length)})");
                                PackingProgress?.Invoke(this, $"输出路径:{actualOutputFile}");
                                OnPackingCompleted();
                            }
                            else
                            {
                                PackingError?.Invoke(this, $"打包失败:未生成输出文件");
                                PackingError?.Invoke(this, $"请检查psarc工具是否正常工作");
                                OnPackingFailed("打包失败:未生成输出文件");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                PackingError?.Invoke(this, "打包操作已取消");
                OnPackingFailed("打包操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"打包失败:{ex.Message}");
                OnPackingFailed($"打包失败:{ex.Message}");
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        public void Repack(string directoryPath)
        {
            RepackAsync(directoryPath).Wait();
        }

        public override void Extract(string directoryPath)
        {
            Repack(directoryPath);
        }
    }
}
