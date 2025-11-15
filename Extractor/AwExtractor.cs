using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class AwExtractor : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static AwExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "wsyster.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.wsyster.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的EXE资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录 {directoryPath} 不存在");
                OnExtractionFailed($"错误:目录 {directoryPath} 不存在");
                return;
            }

            var awFiles = Directory.GetFiles(directoryPath, "*.aw");
            if (awFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.aw文件，无法执行解包");
                OnExtractionFailed("未找到.aw文件，无法执行解包");
                return;
            }

            var wsysFiles = Directory.GetFiles(directoryPath, "*.wsys");
            if (wsysFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.wsys文件，无法执行解包");
                OnExtractionFailed("未找到.wsys文件，无法执行解包");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");
            ExtractionProgress?.Invoke(this, $"找到{awFiles.Length}个.aw文件和{wsysFiles.Length}个.wsys文件");

            string extractDir = Path.Combine(directoryPath, "Extracted");

            int initialFileCount = 0;
            if (Directory.Exists(extractDir))
            {
                initialFileCount = Directory.GetFiles(extractDir, "*.wav").Length;
            }
            else
            {
                Directory.CreateDirectory(extractDir);
            }

            TotalFilesToExtract = wsysFiles.Length; 

            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    foreach (var wsysFilePath in wsysFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(wsysFilePath);
                        ExtractionProgress?.Invoke(this, $"正在处理:{fileName}");

                        int filesBeforeProcessing = Directory.GetFiles(extractDir, "*.wav").Length;

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"\"{wsysFilePath}\"",
                                WorkingDirectory = Path.GetDirectoryName(wsysFilePath),
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };

                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                ExtractionProgress?.Invoke(this, e.Data);
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                ExtractionError?.Invoke(this, $"错误: {e.Data}");
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        int filesAfterProcessing = Directory.GetFiles(extractDir, "*.wav").Length;
                        int newFilesCount = filesAfterProcessing - filesBeforeProcessing;

                        if (newFilesCount > 0)
                        {
                            var allFiles = Directory.GetFiles(extractDir, "*.wav");
                            var newFiles = allFiles.OrderBy(f => new FileInfo(f).CreationTime)
                                                  .Skip(filesBeforeProcessing)
                                                  .Take(newFilesCount);

                            foreach (var newFile in newFiles)
                            {
                                OnFileExtracted(newFile);
                                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(newFile)}");
                            }

                            ExtractionProgress?.Invoke(this, $"从{fileName}提取出{newFilesCount}个.wav文件");
                        }
                        else
                        {
                            ExtractionProgress?.Invoke(this, $"警告:{fileName}未提取出任何.wav文件");
                        }

                        processedCount++;
                        ExtractionProgress?.Invoke(this, $"进度:{processedCount}/{wsysFiles.Length}个.wsys文件");
                    }

                    var finalFiles = Directory.GetFiles(extractDir, "*.wav");
                    ExtractionProgress?.Invoke(this, $"提取完成，共提取{finalFiles.Length}个.wav文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理失败: {ex.Message}");
                OnExtractionFailed($"处理失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}