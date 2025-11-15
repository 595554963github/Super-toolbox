using System.Diagnostics;

namespace super_toolbox
{
    public class Nds_Repacker : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;
        private int _processedSourceFiles = 0;

        static Nds_Repacker()
        {
            _tempExePath = LoadEmbeddedExe("embedded.ndstool.exe", "ndstool.exe");

            if (string.IsNullOrEmpty(_tempExePath) || !File.Exists(_tempExePath))
            {
                throw new InvalidOperationException("无法加载ndstool.exe，请检查嵌入资源");
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await RepackAsync(directoryPath, cancellationToken);
        }

        public async Task RepackAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            _processedSourceFiles = 0;

            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, "错误:目录不存在");
                OnPackingFailed("错误:目录不存在");
                return;
            }
            var sourceFiles = GetAllSourceFiles(directoryPath);
            if (sourceFiles.Count == 0)
            {
                PackingError?.Invoke(this, "未找到可打包NDS的源文件");
                OnPackingFailed("未找到可打包NDS的源文件");
                return;
            }
            TotalFilesToPack = sourceFiles.Count;
            PackingStarted?.Invoke(this, $"开始打包NDS文件，共找到{sourceFiles.Count}个源文件");
            try
            {
                foreach (var file in sourceFiles)
                {
                    FileInfo fileInfo = new FileInfo(file);
                    PackingProgress?.Invoke(this, $"源文件:{Path.GetFileName(file)} ({FormatFileSize(fileInfo.Length)})");
                    _processedSourceFiles++;
                    OnFilePacked(file);
                }
                string parentOutputDir = Path.GetDirectoryName(directoryPath) ?? directoryPath;

                if (CheckRequiredFiles(directoryPath))
                {
                    PackingProgress?.Invoke(this, $"正在将目录打包为NDS文件:{Path.GetFileName(directoryPath)}");
                    if (await ProcessDirectory(directoryPath, parentOutputDir, cancellationToken))
                    {
                        OnPackingCompleted();
                    }
                    return;
                }
                var validDirs = Directory.GetDirectories(directoryPath)
                    .Where(dir => CheckRequiredFiles(dir))
                    .ToList();
                if (validDirs.Count == 0)
                {
                    PackingError?.Invoke(this, "未找到包含完整NDS文件的目录结构");
                    OnPackingFailed("未找到包含完整NDS文件的目录结构");
                    return;
                }
                int successfullyPackedCount = 0;
                foreach (var dir in validDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await ProcessDirectory(dir, parentOutputDir, cancellationToken))
                    {
                        successfullyPackedCount++;
                    }
                }
                if (successfullyPackedCount > 0)
                {
                    PackingProgress?.Invoke(this, $"打包完成!共处理{sourceFiles.Count}个源文件，成功生成{successfullyPackedCount}个NDS文件");
                    OnPackingCompleted();
                }
                else
                {
                    PackingError?.Invoke(this, "所有文件打包NDS都失败了");
                    OnPackingFailed("所有文件打包NDS都失败了");
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

        private List<string> GetAllSourceFiles(string directoryPath)
        {
            var sourceFiles = new List<string>();
            try
            {
                var requiredFiles = new string[]
                {
                    "arm9.bin", "arm7.bin", "y9.bin", "y7.bin",
                    "banner.bin", "header.bin"
                };
                foreach (var file in requiredFiles)
                {
                    string filePath = Path.Combine(directoryPath, file);
                    if (File.Exists(filePath))
                    {
                        sourceFiles.Add(filePath);
                    }
                }
                string dataDir = Path.Combine(directoryPath, "data");
                if (Directory.Exists(dataDir))
                {
                    sourceFiles.AddRange(Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories));
                }
                string overlayDir = Path.Combine(directoryPath, "overlay");
                if (Directory.Exists(overlayDir))
                {
                    sourceFiles.AddRange(Directory.GetFiles(overlayDir, "*", SearchOption.AllDirectories));
                }
                var subDirs = Directory.GetDirectories(directoryPath);
                foreach (var subDir in subDirs)
                {
                    if (CheckRequiredFiles(subDir))
                    {
                        foreach (var file in requiredFiles)
                        {
                            string filePath = Path.Combine(subDir, file);
                            if (File.Exists(filePath))
                            {
                                sourceFiles.Add(filePath);
                            }
                        }
                        string subDataDir = Path.Combine(subDir, "data");
                        if (Directory.Exists(subDataDir))
                        {
                            sourceFiles.AddRange(Directory.GetFiles(subDataDir, "*", SearchOption.AllDirectories));
                        }
                        string subOverlayDir = Path.Combine(subDir, "overlay");
                        if (Directory.Exists(subOverlayDir))
                        {
                            sourceFiles.AddRange(Directory.GetFiles(subOverlayDir, "*", SearchOption.AllDirectories));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"获取源文件时出错:{ex.Message}");
            }
            return sourceFiles;
        }

        private async Task<bool> ProcessDirectory(string sourceDir, string outputDir, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string ndsFileName = Path.GetFileName(sourceDir) + ".nds";
            string outputPath = Path.Combine(outputDir, ndsFileName);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            string arm9Path = Path.Combine(sourceDir, "arm9.bin");
            string arm7Path = Path.Combine(sourceDir, "arm7.bin");
            string y9Path = Path.Combine(sourceDir, "y9.bin");
            string y7Path = Path.Combine(sourceDir, "y7.bin");
            string dataPath = Path.Combine(sourceDir, "data");
            string overlayPath = Path.Combine(sourceDir, "overlay");
            string bannerPath = Path.Combine(sourceDir, "banner.bin");
            string headerPath = Path.Combine(sourceDir, "header.bin");
            string arguments = $"-c \"{outputPath}\" " +
                              $"-9 \"{arm9Path}\" " +
                              $"-7 \"{arm7Path}\" " +
                              $"-y9 \"{y9Path}\" " +
                              $"-y7 \"{y7Path}\" " +
                              $"-d \"{dataPath}\" " +
                              $"-y \"{overlayPath}\" " +
                              $"-t \"{bannerPath}\" " +
                              $"-h \"{headerPath}\"";
            PackingProgress?.Invoke(this, $"正在打包NDS文件:{ndsFileName}");
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(_tempExePath) ?? sourceDir
                }
            })
            {
                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken);
                string output = await outputTask;
                string error = await errorTask;
                if (!string.IsNullOrEmpty(output))
                {
                    foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        PackingProgress?.Invoke(this, line);
                    }
                }
                if (process.ExitCode != 0)
                {
                    PackingError?.Invoke(this, $"打包失败{ndsFileName}: {error}");
                    return false;
                }
            }
            if (File.Exists(outputPath))
            {
                FileInfo fileInfo = new FileInfo(outputPath);
                PackingProgress?.Invoke(this, $"打包完成:{ndsFileName} ({FormatFileSize(fileInfo.Length)})");
                return true;
            }
            else
            {
                PackingError?.Invoke(this, $"打包失败:未生成输出文件{ndsFileName}");
                return false;
            }
        }

        private bool CheckRequiredFiles(string directory)
        {
            return File.Exists(Path.Combine(directory, "arm9.bin")) &&
                   File.Exists(Path.Combine(directory, "arm7.bin")) &&
                   File.Exists(Path.Combine(directory, "y9.bin")) &&
                   File.Exists(Path.Combine(directory, "y7.bin")) &&
                   Directory.Exists(Path.Combine(directory, "data")) &&
                   Directory.Exists(Path.Combine(directory, "overlay")) &&
                   File.Exists(Path.Combine(directory, "banner.bin")) &&
                   File.Exists(Path.Combine(directory, "header.bin"));
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