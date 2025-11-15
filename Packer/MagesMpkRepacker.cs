using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class MagesMpkRepacker : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;
        static MagesMpkRepacker()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "mpk.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.mpk.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的MPK工具资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
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
                PackingError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnPackingFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                                   .Where(f => !f.EndsWith(".mpk", StringComparison.OrdinalIgnoreCase))
                                   .ToList();
            if (allFiles.Count == 0)
            {
                PackingError?.Invoke(this, "未找到可打包的文件");
                OnPackingFailed("未找到可打包的文件");
                return;
            }
            TotalFilesToPack = allFiles.Count;

            PackingStarted?.Invoke(this, $"开始打包{allFiles.Count}个文件到单个MPK文件");
            PackingProgress?.Invoke(this, "要打包的文件列表:");
            foreach (var file in allFiles)
            {
                string relativePath = GetRelativePath(directoryPath, file);
                PackingProgress?.Invoke(this, $"  {relativePath}");
            }

            try
            {
                await CreateSingleMpkFile(directoryPath, allFiles, cancellationToken);
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
        private async Task CreateSingleMpkFile(string baseDirectory, List<string> allFiles, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string baseDirName = Path.GetFileName(baseDirectory);
            if (string.IsNullOrEmpty(baseDirName))
            {
                baseDirName = "packed";
            }
            string parentDirectory = Directory.GetParent(baseDirectory)?.FullName ?? baseDirectory;
            string mpkFileName = baseDirName + ".mpk";
            string outputPath = Path.Combine(parentDirectory, mpkFileName);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            PackingProgress?.Invoke(this, $"正在创建MPK文件:{mpkFileName}，包含{allFiles.Count}个文件");

            string arguments = $"-c \"{baseDirectory}\"";

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
                    WorkingDirectory = parentDirectory
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
                    foreach (string line in error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!string.IsNullOrEmpty(line))
                        {
                            PackingError?.Invoke(this, $"错误:{line}");
                        }
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
                PackingProgress?.Invoke(this, $"打包完成:{mpkFileName} ({FormatFileSize(fileInfo.Length)})");
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