using System.Diagnostics;

namespace super_toolbox
{
    public class IdeaFactory_PacRepacker : BaseExtractor
    {
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        private static string _tempExePath;

        static IdeaFactory_PacRepacker()
        {
            _tempExePath = LoadEmbeddedExe("embedded.pactool.exe", "pactool.exe");
        }

        public override async System.Threading.Tasks.Task ExtractAsync(string directoryPath, System.Threading.CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnPackingFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*", System.IO.SearchOption.AllDirectories).ToList();
            if (allFiles.Count == 0)
            {
                PackingError?.Invoke(this, "未找到可打包的文件");
                OnPackingFailed("未找到可打包的文件");
                return;
            }

            TotalFilesToPack = allFiles.Count;
            PackingStarted?.Invoke(this, $"开始打包{allFiles.Count}个文件到PAC文件");
            PackingProgress?.Invoke(this, "要打包的文件列表:");
            foreach (var file in allFiles)
            {
                string relativePath = GetRelativePath(directoryPath, file);
                PackingProgress?.Invoke(this, $"{relativePath}");
            }

            try
            {
                await CreateSinglePacFile(directoryPath, allFiles, cancellationToken);
                OnPackingCompleted();
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

        private async System.Threading.Tasks.Task CreateSinglePacFile(string baseDirectory, System.Collections.Generic.List<string> allFiles, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string baseDirName = Path.GetFileName(baseDirectory);
            if (string.IsNullOrEmpty(baseDirName))
            {
                baseDirName = "packed";
            }

            string parentDirectory = Directory.GetParent(baseDirectory)?.FullName ?? baseDirectory;
            string pacFileName = baseDirName + ".pac";
            string outputPath = Path.Combine(parentDirectory, pacFileName);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            PackingProgress?.Invoke(this, $"正在创建PAC文件:{pacFileName},包含{allFiles.Count}个文件");

            foreach (var sourceFile in allFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                OnFilePacked(sourceFile);
                PackingProgress?.Invoke(this, $"正在打包:{Path.GetFileName(sourceFile)}");
            }

            await System.Threading.Tasks.Task.Run(() =>
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"-p \"{baseDirectory}\"",
                    WorkingDirectory = parentDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        throw new Exception($"无法启动打包进程:{pacFileName}");
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            PackingProgress?.Invoke(this, e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            PackingError?.Invoke(this, $"错误:{e.Data}");
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"打包失败,错误代码:{process.ExitCode}");
                    }
                }

                if (!File.Exists(outputPath))
                {
                    throw new FileNotFoundException("打包过程未生成PAC文件", pacFileName);
                }
            }, cancellationToken);

            if (File.Exists(outputPath))
            {
                FileInfo fileInfo = new FileInfo(outputPath);
                PackingProgress?.Invoke(this, $"打包完成:{Path.GetFileName(outputPath)} ({FormatFileSize(fileInfo.Length)})");
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

        private string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar);

            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
