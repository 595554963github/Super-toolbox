using System.Diagnostics;

namespace super_toolbox
{
    public class Png2astc_Converter : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        static Png2astc_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.astcenc-avx2.exe", "astcenc-avx2.exe");
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
            TotalFilesToConvert = Directory.GetFiles(directoryPath, "*.png", SearchOption.AllDirectories).Length;

            var pngFiles = Directory.EnumerateFiles(directoryPath, "*.png", SearchOption.AllDirectories);
            int successCount = 0;

            try
            {
                foreach (var pngFilePath in pngFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(pngFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(pngFilePath);
                    string fileDirectory = Path.GetDirectoryName(pngFilePath) ?? string.Empty;

                    string astcFilePath = Path.Combine(fileDirectory, $"{fileName}.astc");

                    try
                    {
                        var processStartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"-cs \"{pngFilePath}\" \"{astcFilePath}\" 6x6 -medium",
                            WorkingDirectory = fileDirectory,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        using (var process = Process.Start(processStartInfo))
                        {
                            if (process == null)
                            {
                                ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(pngFilePath)}");
                                OnConversionFailed($"无法启动转换进程:{pngFilePath}");
                                continue;
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

                            if (process.ExitCode != 0)
                            {
                                ConversionError?.Invoke(this, $"{fileName}.png转换失败,错误代码:{process.ExitCode}");
                                OnConversionFailed($"{fileName}.png 转换失败,错误代码:{process.ExitCode}");
                                continue;
                            }
                        }

                        if (File.Exists(astcFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(astcFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(astcFilePath)}");
                            OnFileConverted(astcFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.png转换成功,但未找到输出文件");
                            OnConversionFailed($"{fileName}.png转换成功,但未找到输出文件");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.png处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
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
    }
}
