using System.Diagnostics;
using System.Reflection;

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
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "Unpsarc.exe");
            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.Unpsarc.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的Unpsarc.exe资源未找到");
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
            _processedSourceFiles = 0;
            _totalSourceFiles = 0;

            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, "错误:目录不存在");
                OnPackingFailed("错误:目录不存在");
                return;
            }
            PackingProgress?.Invoke(this, "===========================================");
            PackingProgress?.Invoke(this, "• 本打包器只能打包子文件夹中的文件");
            PackingProgress?.Invoke(this, "• 请将要打包的文件放在子文件夹中");
            PackingProgress?.Invoke(this, "• 当前目录下的文件不会被直接打包");
            PackingProgress?.Invoke(this, "===========================================");

            var directories = Directory.GetDirectories(directoryPath);
            if (directories.Length == 0)
            {
                PackingError?.Invoke(this, "未找到可打包的文件夹");
                PackingError?.Invoke(this, "提示:请创建子文件夹并将要打包的文件放入其中");
                OnPackingFailed("未找到可打包的文件夹");
                return;
            }

            PackingProgress?.Invoke(this, $"找到{directories.Length}个子文件夹:");
            foreach (var dir in directories)
            {
                var fileCount = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
                PackingProgress?.Invoke(this, $"  - {Path.GetFileName(dir)} (包含{fileCount}个文件)");
            }

            foreach (var folderPath in directories)
            {
                var filesInFolder = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                _totalSourceFiles += filesInFolder.Length;
            }
            TotalFilesToPack = _totalSourceFiles;
            PackingStarted?.Invoke(this, $"开始打包PSARC文件，共找到{_totalSourceFiles}个源文件");
            try
            {
                int successfullyPackedCount = 0;
                foreach (var folderPath in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string folderName = Path.GetFileName(folderPath);
                    string parentDir = Path.GetDirectoryName(folderPath) ?? Directory.GetCurrentDirectory();
                    var filesInCurrentFolder = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);

                    PackingProgress?.Invoke(this, $"正在打包文件夹: {folderName} (包含{filesInCurrentFolder.Length}个文件)");
                    PackingProgress?.Invoke(this, $"输出文件: {folderName}.psarc");

                    foreach (var file in filesInCurrentFolder)
                    {
                        PackingProgress?.Invoke(this, $"处理源文件: {Path.GetRelativePath(folderPath, file)}");
                        _processedSourceFiles++;
                        OnFilePacked(file);
                    }
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"-c \"{folderName}\"",
                            WorkingDirectory = parentDir,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
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
                        PackingError?.Invoke(this, $"打包失败{folderName}:{error}");
                        OnPackingFailed($"打包失败{folderName}: {error}");
                        continue;
                    }
                    string expectedPsarcFile = Path.Combine(parentDir, folderName + ".psarc");
                    if (File.Exists(expectedPsarcFile))
                    {
                        FileInfo fileInfo = new FileInfo(expectedPsarcFile);
                        PackingProgress?.Invoke(this, $"打包完成:{folderName}.psarc({FormatFileSize(fileInfo.Length)})");
                        successfullyPackedCount++;
                    }
                    else
                    {
                        PackingError?.Invoke(this, $"打包失败:未生成输出文件{folderName}.psarc");
                        OnPackingFailed($"打包失败:未生成输出文件{folderName}.psarc");
                    }
                }
                if (successfullyPackedCount > 0)
                {
                    PackingProgress?.Invoke(this, $"打包完成!共处理{_processedSourceFiles}个源文件，成功生成{successfullyPackedCount}个PSARC文件");
                    PackingProgress?.Invoke(this, "输出文件位于所选目录的父目录中");
                    OnPackingCompleted();
                }
                else
                {
                    PackingError?.Invoke(this, "所有文件夹打包PSARC都失败了");
                    OnPackingFailed("所有文件夹打包PSARC都失败了");
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