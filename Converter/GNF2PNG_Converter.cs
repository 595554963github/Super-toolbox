using System.Diagnostics;

namespace super_toolbox
{
    public class GNF2PNG_Converter : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        static GNF2PNG_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.GNF2PNG.exe", "GNF2PNG.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> convertedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath} 不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            TotalFilesToConvert = Directory.GetFiles(directoryPath, "*.gnf", SearchOption.AllDirectories).Length;

            var gnfFiles = Directory.EnumerateFiles(directoryPath, "*.gnf", SearchOption.AllDirectories);
            int successCount = 0;

            try
            {
                foreach (var gnfFilePath in gnfFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(gnfFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(gnfFilePath);
                    string fileDirectory = Path.GetDirectoryName(gnfFilePath) ?? string.Empty;

                    fileName = fileName.Replace(".png", "", StringComparison.OrdinalIgnoreCase);

                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    try
                    {
                        bool conversionSuccess = await ConvertGnfToPng(gnfFilePath, pngFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess && File.Exists(pngFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(pngFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngFilePath)}");
                            OnFileConverted(pngFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.gnf转换失败");
                            OnConversionFailed($"{fileName}.gnf转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.gnf处理错误:{ex.Message}");
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

        private async Task<bool> ConvertGnfToPng(string gnfFilePath, string pngFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"\"{gnfFilePath}\"",
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
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(gnfFilePath)}");
                        return false;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionProgress?.Invoke(this, $"[GNF2PNG] {e.Data}");
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionError?.Invoke(this, $"[GNF2PNG]错误:{e.Data}");
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0)
                    {
                        return File.Exists(pngFilePath);
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"转换失败，错误代码:{process.ExitCode}");
                        if (File.Exists(pngFilePath))
                        {
                            try { File.Delete(pngFilePath); } catch { }
                        }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常: {ex.Message}");
                if (File.Exists(pngFilePath))
                {
                    try { File.Delete(pngFilePath); } catch { }
                }
                return false;
            }
        }
    }
}