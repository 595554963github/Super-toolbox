using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class Darc_Repacker : BaseExtractor
    {
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        private static string _tempExePath;

        static Darc_Repacker()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _tempExePath = LoadEmbeddedExe("embedded.darctool.exe", "darctool.exe");

            if (string.IsNullOrEmpty(_tempExePath) || !File.Exists(_tempExePath))
            {
                throw new InvalidOperationException("无法加载darctool.exe，请检查嵌入资源");
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, $"错误:目录 {directoryPath} 不存在");
                OnPackingFailed($"错误:目录 {directoryPath} 不存在");
                return;
            }
            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories).ToList();
            if (allFiles.Count == 0)
            {
                PackingError?.Invoke(this, "未找到可打包的文件");
                OnPackingFailed("未找到可打包的文件");
                return;
            }
            TotalFilesToPack = allFiles.Count;

            PackingStarted?.Invoke(this, $"开始打包{allFiles.Count}个文件到单个DARC文件");
            PackingProgress?.Invoke(this, "要打包的文件列表:");
            foreach (var file in allFiles)
            {
                string relativePath = GetRelativePath(directoryPath, file);
                PackingProgress?.Invoke(this, $"  {relativePath}");
            }

            try
            {
                await CreateSingleDarcFile(directoryPath, allFiles, cancellationToken);
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

        private async Task CreateSingleDarcFile(string baseDirectory, List<string> allFiles, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string baseDirName = Path.GetFileName(baseDirectory);
            if (string.IsNullOrEmpty(baseDirName))
            {
                baseDirName = "packed";
            }
            string parentDirectory = Directory.GetParent(baseDirectory)?.FullName ?? baseDirectory;
            string darcFileName = baseDirName + ".arc";
            string outputPath = Path.Combine(parentDirectory, darcFileName);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            PackingProgress?.Invoke(this, $"正在创建DARC文件:{darcFileName}，包含{allFiles.Count}个文件");

            string arguments = $"-cvfd \"{outputPath}\" \"{baseDirectory}\"";

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
                    StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                    StandardErrorEncoding = Encoding.GetEncoding("GBK"),
                    WorkingDirectory = baseDirectory
                }
            })
            {
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                await process.WaitForExitAsync(cancellationToken);
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        PackingProgress?.Invoke(this, line);
                    }
                }
                if (!string.IsNullOrEmpty(error))
                {
                    var errorLines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => !line.ToLower().Contains("ignore_darctool.txt"))
                        .ToList();

                    foreach (string line in errorLines)
                    {
                        PackingError?.Invoke(this, $"错误:{line}");
                    }
                }

                if (process.ExitCode != 0)
                {
                    throw new Exception($"打包失败(ExitCode:{process.ExitCode})");
                }
            }

            if (File.Exists(outputPath))
            {
                FileInfo fileInfo = new FileInfo(outputPath);
                PackingProgress?.Invoke(this, $"打包完成:{darcFileName} ({FormatFileSize(fileInfo.Length)})");
                foreach (var file in allFiles)
                {
                    OnFilePacked(file);
                }
            }
            else
            {
                throw new FileNotFoundException("打包过程未生成输出文件", outputPath);
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