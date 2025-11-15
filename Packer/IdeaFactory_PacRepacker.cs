using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class IdeaFactory_PacRepacker : BaseExtractor
    {
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        private static string _tempExePath;
        private static string _tempDllPath;

        static IdeaFactory_PacRepacker()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _tempExePath = LoadEmbeddedExe("embedded.pac_repack.exe", "pac_repack.exe");
            _tempDllPath = Path.Combine(TempDllDirectory, "libpac.dll");

            if (!File.Exists(_tempDllPath))
            {
                LoadEmbeddedDll("embedded.libpac.dll", "libpac.dll");
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

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
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
                PackingProgress?.Invoke(this, $"  {relativePath}");
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

        private async Task CreateSinglePacFile(string baseDirectory, List<string> allFiles, CancellationToken cancellationToken)
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

            PackingProgress?.Invoke(this, $"正在创建PAC文件:{pacFileName}，包含{allFiles.Count}个文件");

            string arguments = $"\"{baseDirectory}\"";

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
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = baseDirectory
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
                        if (!string.IsNullOrEmpty(line))
                        {
                            PackingProgress?.Invoke(this, line);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(error))
                {
                    var errorLines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
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
            string[] possiblePaths = {
                Path.Combine(baseDirectory, "data.pac"),
                Path.Combine(parentDirectory, "data.pac"),
                Path.Combine(baseDirectory, pacFileName),
                outputPath
            };

            string? pacFilePath = possiblePaths.FirstOrDefault(File.Exists);

            if (!string.IsNullOrEmpty(pacFilePath) && File.Exists(pacFilePath))
            {
                FileInfo fileInfo = new FileInfo(pacFilePath);
                PackingProgress?.Invoke(this, $"打包完成:{Path.GetFileName(pacFilePath)} ({FormatFileSize(fileInfo.Length)})");
                foreach (var file in allFiles)
                {
                    OnFilePacked(file);
                }
            }
            else
            {
                throw new FileNotFoundException("打包过程未生成PAC文件", pacFileName);
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