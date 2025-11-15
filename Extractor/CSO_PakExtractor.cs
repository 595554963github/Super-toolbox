using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class CSO_PakExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempExePath;

        static CSO_PakExtractor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _tempExePath = LoadEmbeddedExe("embedded.csopak.exe", "csopak.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var pakFiles = Directory.GetFiles(directoryPath, "*.pak");
            if (pakFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.pak文件");
                OnExtractionFailed("未找到.pak文件");
                return;
            }

            TotalFilesToExtract = pakFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{pakFiles.Length}个PAK文件");

            try
            {
                await Task.Run(() =>
                {
                    int totalExtractedFiles = 0; 

                    foreach (var pakFilePath in pakFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(pakFilePath)}");

                        try
                        {
                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"\"{pakFilePath}\"",
                                    WorkingDirectory = Path.GetDirectoryName(pakFilePath),
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                                    StandardErrorEncoding = Encoding.GetEncoding("GBK")
                                }
                            };

                            process.Start();
                            string output = process.StandardOutput.ReadToEnd();
                            string errorOutput = process.StandardError.ReadToEnd();

                            process.WaitForExit();

                            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (!string.IsNullOrEmpty(line))
                                {
                                    ExtractionProgress?.Invoke(this, line);
                                }
                            }

                            if (!string.IsNullOrEmpty(errorOutput))
                            {
                                foreach (string line in errorOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    ExtractionError?.Invoke(this, $"错误:{line}");
                                }
                            }

                            if (process.ExitCode == 0)
                            {
                                string workingDir = Path.GetDirectoryName(pakFilePath) ?? directoryPath;
                                string extractDir = Path.Combine(workingDir, "lstrike");

                                if (Directory.Exists(extractDir))
                                {
                                    var extractedFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
                                    totalExtractedFiles += extractedFiles.Length; 

                                    foreach (var file in extractedFiles)
                                    {
                                        string fileName = Path.GetFileName(file);
                                        OnFileExtracted(fileName);
                                        ExtractionProgress?.Invoke(this, $"已提取:{fileName}");
                                    }

                                    ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(pakFilePath)} -> {extractedFiles.Length}个文件");
                                }
                            }
                            else
                            {
                                ExtractionError?.Invoke(this, $"处理文件{Path.GetFileName(pakFilePath)}失败，退出代码:{process.ExitCode}");
                                if (!string.IsNullOrEmpty(errorOutput))
                                {
                                    ExtractionError?.Invoke(this, $"错误详情:{errorOutput}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(pakFilePath)}处理错误: {ex.Message}");
                            OnExtractionFailed($"文件{Path.GetFileName(pakFilePath)} 处理错误:{ex.Message}");
                        }
                    }
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}