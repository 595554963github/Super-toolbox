using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace super_toolbox
{
    public class XWBPacker : BaseExtractor
    {
        private static string _tempExePath;

        static XWBPacker()
        {
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
                OnExtractionFailed("错误：输入目录不存在");
                return;
            }

            var wavFiles = Directory.GetFiles(inputDirectory, "*.wav", SearchOption.AllDirectories);
            if (wavFiles.Length == 0)
            {
                OnExtractionFailed("未找到.wav文件");
                return;
            }

            TotalFilesToExtract = 1;

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
                        OnExtractionFailed("无法确定父目录路径");
                        return;
                    }

                    string outputPath = Path.Combine(parentDirectory, $"{folderName}.xwb");

                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }

                    StringBuilder arguments = new StringBuilder();
                    arguments.Append($"-o \"{outputPath}\" ");

                    foreach (var wavFile in wavFiles)
                    {
                        arguments.Append($"\"{wavFile}\" ");
                    }

                    string command = arguments.ToString().Trim();

                    Console.WriteLine($"执行命令: {_tempExePath} {command}");

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = command,
                            WorkingDirectory = inputDirectory,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    StringBuilder outputBuilder = new StringBuilder();
                    StringBuilder errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            Console.WriteLine($"XWBTool输出: {e.Data}");
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorBuilder.AppendLine(e.Data);
                            Console.WriteLine($"XWBTool错误: {e.Data}");
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    process.WaitForExit();

                    Console.WriteLine($"进程退出代码: {process.ExitCode}");
                    Console.WriteLine($"输出文件是否存在: {File.Exists(outputPath)}");
                    Console.WriteLine($"输出文件大小: {(File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0)} 字节");

                    if (process.ExitCode == 0 && File.Exists(outputPath))
                    {
                        OnFileExtracted(outputPath);
                        OnExtractionCompleted();
                    }
                    else
                    {
                        string errorMsg = $"XWBTool处理失败，退出代码: {process.ExitCode}";
                        if (errorBuilder.Length > 0)
                        {
                            errorMsg += $", 错误: {errorBuilder}";
                        }
                        OnExtractionFailed(errorMsg);
                    }

                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"打包失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            PackWavFilesToParentDirectoryAsync(directoryPath).Wait();
        }

        public void PackWavFilesToParentDirectory(string inputDirectory)
        {
            PackWavFilesToParentDirectoryAsync(inputDirectory).Wait();
        }
    }
}