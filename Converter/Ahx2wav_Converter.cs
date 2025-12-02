using System.Diagnostics;

namespace super_toolbox
{
    public class Ahx2wav_Converter : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        static Ahx2wav_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.ahx2wav.exe", "ahx2wav.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var ahxFiles = Directory.GetFiles(directoryPath, "*.ahx", SearchOption.AllDirectories);
            TotalFilesToConvert = ahxFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var ahxFilePath in ahxFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(ahxFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.ahx");

                    string fileDirectory = Path.GetDirectoryName(ahxFilePath) ?? string.Empty;

                    try
                    {
                        string ahxWavFile = Path.Combine(fileDirectory, $"{fileName}.ahx.wav");
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(ahxWavFile))
                            File.Delete(ahxWavFile);
                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await ConvertAhxToWav(ahxFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess)
                        {
                            if (File.Exists(ahxWavFile))
                            {
                                File.Move(ahxWavFile, wavFile);
                                successCount++;
                                ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                                OnFileConverted(wavFile);
                            }
                            else if (File.Exists(wavFile))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                                OnFileConverted(wavFile);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.ahx转换成功但未找到输出文件");
                                OnConversionFailed($"{fileName}.ahx转换成功但未找到输出文件");
                            }
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.ahx转换失败");
                            OnConversionFailed($"{fileName}.ahx转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.ahx处理错误:{ex.Message}");
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

        private async Task<bool> ConvertAhxToWav(string ahxFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"\"{ahxFilePath}\"",
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
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(ahxFilePath)}");
                        return false;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionProgress?.Invoke(this, $"[ahx2wav] {e.Data}");
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionError?.Invoke(this, $"[ahx2wav]错误:{e.Data}");
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return false;
            }
        }
    }
}