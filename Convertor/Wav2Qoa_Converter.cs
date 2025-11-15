using System.Diagnostics;

namespace super_toolbox
{
    public class Wav2Qoa_Converter : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        static Wav2Qoa_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.wav2qoa.exe", "wav2qoa.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> convertedFiles = new List<string>();
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var wavFiles = Directory.EnumerateFiles(directoryPath, "*.wav", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = wavFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(wavFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                    fileName = fileName.Replace(".qoa", "", StringComparison.OrdinalIgnoreCase);

                    string qoaFilePath = Path.Combine(fileDirectory, $"{fileName}.qoa");

                    try
                    {
                        bool conversionSuccess = await ConvertWavToQoa(wavFilePath, qoaFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess && File.Exists(qoaFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(qoaFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(qoaFilePath)}");
                            OnFileConverted(qoaFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.wav转换失败");
                            OnConversionFailed($"{fileName}.wav转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.wav处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成，成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成，但未成功转换任何文件");
                }

                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }

        private async Task<bool> ConvertWavToQoa(string wavFilePath, string qoaFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"\"{wavFilePath}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(wavFilePath)}");
                        return false;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionProgress?.Invoke(this, e.Data);
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionError?.Invoke(this, $"错误:{e.Data}");
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0)
                    {
                        return File.Exists(qoaFilePath);
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"转换失败，错误代码:{process.ExitCode}");
                        if (File.Exists(qoaFilePath))
                        {
                            try { File.Delete(qoaFilePath); } catch { }
                        }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                if (File.Exists(qoaFilePath))
                {
                    try { File.Delete(qoaFilePath); } catch { }
                }
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}