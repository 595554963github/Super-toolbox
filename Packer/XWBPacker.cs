using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class XWBPacker : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        static XWBPacker()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "XWBTool.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.XWBTool.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的XWBTool资源未找到");
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await PackWavFilesToParentDirectoryAsync(directoryPath, cancellationToken);
        }

        public async Task PackWavFilesToParentDirectoryAsync(string inputDirectory, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(inputDirectory))
            {
                PackingError?.Invoke(this, "错误:输入目录不存在");
                OnPackingFailed("错误:输入目录不存在");
                return;
            }
            var wavFiles = Directory.GetFiles(inputDirectory, "*.wav", SearchOption.AllDirectories);
            if (wavFiles.Length == 0)
            {
                PackingError?.Invoke(this, "未找到.wav文件");
                OnPackingFailed("未找到.wav文件");
                return;
            }
            TotalFilesToPack = wavFiles.Length;
            PackingStarted?.Invoke(this, $"开始打包{wavFiles.Length}个WAV文件到XWB文件");
            PackingProgress?.Invoke(this, "要打包的WAV文件列表:");
            foreach (var file in wavFiles)
            {
                string fileName = Path.GetFileName(file);
                FileInfo fileInfo = new FileInfo(file);
                PackingProgress?.Invoke(this, $"准备添加:{fileName} ({FormatFileSize(fileInfo.Length)})");
            }
            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string folderName = Path.GetFileName(inputDirectory.TrimEnd(Path.DirectorySeparatorChar));
                    if (string.IsNullOrEmpty(folderName))
                    {
                        folderName = "output";
                    }
                    string? parentDirectory = Directory.GetParent(inputDirectory)?.FullName;
                    if (string.IsNullOrEmpty(parentDirectory))
                    {
                        PackingError?.Invoke(this, "无法确定父目录路径");
                        OnPackingFailed("无法确定父目录路径");
                        return;
                    }
                    string outputPath = Path.Combine(parentDirectory, $"{folderName}.xwb");
                    PackingProgress?.Invoke(this, $"输出文件路径:{outputPath}");
                    if (File.Exists(outputPath))
                    {
                        PackingProgress?.Invoke(this, "删除已存在的输出文件");
                        File.Delete(outputPath);
                    }
                    StringBuilder arguments = new StringBuilder();
                    arguments.Append($"-o \"{outputPath}\" ");
                    foreach (var wavFile in wavFiles)
                    {
                        arguments.Append($"\"{wavFile}\" ");
                    }
                    string command = arguments.ToString().Trim();
                    PackingProgress?.Invoke(this, $"执行命令:{_tempExePath} {command}");
                    PackingProgress?.Invoke(this, $"正在打包XWB文件:{Path.GetFileName(outputPath)}");
                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = command,
                        WorkingDirectory = inputDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                        StandardErrorEncoding = Encoding.GetEncoding("GBK")
                    };
                    PackingProgress?.Invoke(this, "正在启动外部进程...");
                    using (var process = new Process())
                    {
                        process.StartInfo = processStartInfo;
                        StringBuilder outputBuilder = new StringBuilder();
                        StringBuilder errorBuilder = new StringBuilder();
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                PackingProgress?.Invoke(this, $"工具输出:{e.Data}");
                                outputBuilder.AppendLine(e.Data);
                            }
                        };
                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                PackingError?.Invoke(this, $"工具错误:{e.Data}");
                                errorBuilder.AppendLine(e.Data);
                            }
                        };
                        bool started = process.Start();
                        if (!started)
                        {
                            throw new Exception("无法启动XWB打包进程");
                        }

                        PackingProgress?.Invoke(this, "进程已启动，开始读取输出...");
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                        PackingProgress?.Invoke(this, $"进程退出代码:{process.ExitCode}");
                        if (process.ExitCode != 0)
                        {
                            string errorDetails = errorBuilder.Length > 0 ? errorBuilder.ToString() : "无错误详情";
                            throw new Exception($"XWB打包失败(ExitCode:{process.ExitCode})。错误:{errorDetails}");
                        }
                    }
                    if (File.Exists(outputPath))
                    {
                        FileInfo fileInfo = new FileInfo(outputPath);
                        PackingProgress?.Invoke(this, "打包完成!");
                        PackingProgress?.Invoke(this, $"输出文件:{Path.GetFileName(outputPath)}");
                        PackingProgress?.Invoke(this, $"文件大小:{FormatFileSize(fileInfo.Length)}");
                        PackingProgress?.Invoke(this, $"包含WAV文件数:{wavFiles.Length}");
                        foreach (var file in wavFiles)
                        {
                            OnFilePacked(file);
                        }
                        OnPackingCompleted();
                    }
                    else
                    {
                        throw new FileNotFoundException("XWB打包过程未生成输出文件", outputPath);
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

        public void PackWavFilesToParentDirectory(string inputDirectory)
        {
            PackWavFilesToParentDirectoryAsync(inputDirectory).Wait();
        }

        public override void Extract(string directoryPath)
        {
            PackWavFilesToParentDirectory(directoryPath);
        }
    }
}