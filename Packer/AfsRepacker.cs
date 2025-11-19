using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class AfsRepacker : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        static AfsRepacker()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _tempExePath = LoadEmbeddedExe("embedded.afs_util.exe", "afs_util.exe");

            if (string.IsNullOrEmpty(_tempExePath) || !File.Exists(_tempExePath))
            {
                throw new InvalidOperationException("无法加载afs_util.exe，请检查嵌入资源");
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await RepackAsync(directoryPath, cancellationToken);
        }

        public async Task RepackAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, "错误:目录不存在");
                OnPackingFailed("错误:目录不存在");
                return;
            }

            string parentDir = Directory.GetParent(directoryPath)?.FullName ?? directoryPath;
            string folderName = Path.GetFileName(directoryPath);
            string outputAfsPath = Path.Combine(parentDir, folderName + ".afs");

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                                   .Where(f => !f.EndsWith(".afs", StringComparison.OrdinalIgnoreCase))
                                   .ToArray();

            TotalFilesToPack = allFiles.Length;

            PackingStarted?.Invoke(this, $"开始打包目录:{directoryPath}");
            PackingProgress?.Invoke(this, $"找到 {allFiles.Length} 个文件:");

            foreach (var file in allFiles)
            {
                string relativePath = GetRelativePath(directoryPath, file);
                PackingProgress?.Invoke(this, $"  {relativePath}");
            }

            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"-p \"{directoryPath}\"",
                        WorkingDirectory = parentDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    PackingProgress?.Invoke(this, $"正在打包AFS文件:{Path.GetFileName(outputAfsPath)}");

                    using (var process = Process.Start(processStartInfo))
                    {
                        if (process == null)
                        {
                            throw new Exception("无法启动AFS打包进程");
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

                        foreach (var file in allFiles)
                        {
                            ThrowIfCancellationRequested(cancellationToken);
                            OnFilePacked(file);
                        }

                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            throw new Exception($"AFS打包失败(ExitCode:{process.ExitCode})");
                        }
                    }

                    if (File.Exists(outputAfsPath))
                    {
                        FileInfo fileInfo = new FileInfo(outputAfsPath);
                        PackingProgress?.Invoke(this, $"打包完成!");
                        PackingProgress?.Invoke(this, $"输出文件:{Path.GetFileName(outputAfsPath)}");
                        PackingProgress?.Invoke(this, $"文件大小:{FormatFileSize(fileInfo.Length)}");
                        OnPackingCompleted();
                    }
                    else
                    {
                        throw new FileNotFoundException("AFS打包过程未生成输出文件", outputAfsPath);
                    }
                }, cancellationToken);
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

        private string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar);

            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
