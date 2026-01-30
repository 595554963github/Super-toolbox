using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class CPZ7_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempExePath;

        static CPZ7_Extractor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _tempExePath = LoadEmbeddedExe("embedded.cpz7.exe", "cpz7.exe");
        }

        private new static string LoadEmbeddedExe(string resourceName, string outputFileName)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "super_toolbox");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            string exePath = Path.Combine(tempDir, outputFileName);

            if (!File.Exists(exePath))
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null) throw new FileNotFoundException($"嵌入的资源'{resourceName}'未找到");

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                File.WriteAllBytes(exePath, buffer);
            }

            return exePath;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var cpzFiles = Directory.GetFiles(directoryPath, "*.cpz", SearchOption.AllDirectories).ToArray();
            int totalCpzFiles = cpzFiles.Length;
            int currentFileIndex = 0;
            int totalExtractedFiles = 0;

            try
            {
                for (int i = 0; i < totalCpzFiles; i++)
                {
                    var cpzFilePath = cpzFiles[i];
                    currentFileIndex = i + 1;
                    ThrowIfCancellationRequested(cancellationToken);

                    ExtractionProgress?.Invoke(this, $"正在处理({currentFileIndex}/{totalCpzFiles}):{Path.GetFileName(cpzFilePath)}");

                    try
                    {
                        int extractedCount = await ExtractCpzFile(cpzFilePath, cancellationToken);

                        if (extractedCount > 0)
                        {
                            totalExtractedFiles += extractedCount;
                            ExtractionProgress?.Invoke(this, $"已处理({currentFileIndex}/{totalCpzFiles}):{Path.GetFileName(cpzFilePath)} -> 提取出{extractedCount}个文件");
                        }
                        else
                        {
                            ExtractionError?.Invoke(this, $"{Path.GetFileName(cpzFilePath)}提取失败");
                            OnExtractionFailed($"{Path.GetFileName(cpzFilePath)}提取失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"提取异常:{ex.Message}");
                        OnExtractionFailed($"{Path.GetFileName(cpzFilePath)}处理错误:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"提取完成,共提取{totalExtractedFiles}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }

        private async Task<int> ExtractCpzFile(string cpzFilePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                int fileCount = 0;

                try
                {
                    string fileDirectory = Path.GetDirectoryName(cpzFilePath) ?? string.Empty;
                    string cpzFileName = Path.GetFileName(cpzFilePath);
                    string cpzFileNameWithoutExt = Path.GetFileNameWithoutExtension(cpzFilePath);
                    string extractDir = Path.Combine(fileDirectory, cpzFileNameWithoutExt);

                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"\"{cpzFilePath}\"",
                        WorkingDirectory = fileDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                        StandardErrorEncoding = Encoding.GetEncoding("GBK")
                    };

                    using var process = Process.Start(processStartInfo);
                    if (process == null)
                    {
                        ExtractionError?.Invoke(this, $"无法启动解包进程:{cpzFileName}");
                        return 0;
                    }

                    StringBuilder outputBuilder = new StringBuilder();
                    StringBuilder errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            ExtractionProgress?.Invoke(this, e.Data);

                            if (e.Data.Contains("提取:") && e.Data.Contains("]"))
                            {
                                fileCount++;
                                string? fileName = e.Data.Split(' ').LastOrDefault()?.Trim();
                                if (!string.IsNullOrEmpty(fileName))
                                {
                                    OnFileExtracted(Path.Combine(extractDir, fileName));
                                }
                                else
                                {
                                    OnFileExtracted($"{extractDir}/file_{fileCount}");
                                }
                            }
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorBuilder.AppendLine(e.Data);
                            ExtractionError?.Invoke(this, $"错误:{e.Data}");
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        ExtractionError?.Invoke(this, $"解包进程退出代码:{process.ExitCode}");
                        return fileCount;
                    }

                    if (fileCount == 0)
                    {
                        var lines = outputBuilder.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (line.Contains("提取完成!共提取"))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(line, @"共提取\s*(\d+)\s*个");
                                if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                                {
                                    fileCount = count;
                                    break;
                                }
                            }
                        }
                    }

                    return fileCount;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"进程执行异常:{ex.Message}");
                    return fileCount;
                }
            }, cancellationToken);
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}