using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class CmvDecoder : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempExePath;

        static CmvDecoder()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "CMVDecode.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.CMVDecode.exe"))
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
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误目录{directoryPath}不存在");
                return;
            }

            var cmvFiles = Directory.GetFiles(directoryPath, "*.cmv");
            if (cmvFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.cmv文件");
                OnExtractionFailed("未找到.cmv文件");
                return;
            }

            TotalFilesToExtract = cmvFiles.Length * 2;
            ExtractionStarted?.Invoke(this, $"开始处理{cmvFiles.Length}");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var cmvFilePath in cmvFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        ExtractionProgress?.Invoke(this, $"正在解码:{Path.GetFileName(cmvFilePath)}");

                        var startInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"\"{cmvFilePath}\"",
                            WorkingDirectory = Path.GetDirectoryName(cmvFilePath) ?? directoryPath,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                            StandardErrorEncoding = Encoding.GetEncoding("GBK")
                        };

                        StringBuilder outputBuilder = new StringBuilder();
                        using (var process = new Process { StartInfo = startInfo })
                        {
                            process.OutputDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                {
                                    ExtractionProgress?.Invoke(this, e.Data);
                                    outputBuilder.AppendLine(e.Data);
                                }
                            };

                            process.ErrorDataReceived += (sender, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                {
                                    ExtractionError?.Invoke(this, $"错误:{e.Data}");
                                    outputBuilder.AppendLine($"错误:{e.Data}");
                                }
                            };

                            process.Start();
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                            process.WaitForExit();

                            Thread.Sleep(1000);

                            string baseName = Path.GetFileNameWithoutExtension(cmvFilePath);
                            string workingDir = Path.GetDirectoryName(cmvFilePath) ?? directoryPath;

                            string aviFile = Path.Combine(workingDir, baseName + ".avi");
                            string oggFile = Path.Combine(workingDir, baseName + ".ogg");

                            List<string> outputFiles = new List<string>();

                            if (File.Exists(aviFile))
                            {
                                outputFiles.Add(aviFile);
                                ExtractionProgress?.Invoke(this, $"已提取视频文件: {Path.GetFileName(aviFile)}");
                                OnFileExtracted(aviFile);
                            }

                            if (File.Exists(oggFile))
                            {
                                outputFiles.Add(oggFile);
                                ExtractionProgress?.Invoke(this, $"已提取音频文件: {Path.GetFileName(oggFile)}");
                                OnFileExtracted(oggFile);
                            }

                            if (outputFiles.Count == 2)
                            {
                                ExtractionProgress?.Invoke(this, $"成功解码: {Path.GetFileName(cmvFilePath)} -> 2个文件(视频+音频)");
                            }
                            else if (outputFiles.Count == 1)
                            {
                                ExtractionProgress?.Invoke(this, $"部分解码: {Path.GetFileName(cmvFilePath)} -> 只生成了1个文件");
                            }
                            else
                            {
                                ExtractionProgress?.Invoke(this, $"警告:{Path.GetFileName(cmvFilePath)}解码后未找到输出文件");
                            }
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "解码操作已取消");
                OnExtractionFailed("解码操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理失败:{ex.Message}");
                OnExtractionFailed($"处理失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}