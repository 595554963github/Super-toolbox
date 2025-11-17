using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class PVR2PNG_Converter : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        static PVR2PNG_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.PVRTexToolCLI.exe", "PVRTexToolCLI.exe");
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
            var pvrFiles = Directory.EnumerateFiles(directoryPath, "*.pvr", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = pvrFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var pvrFilePath in pvrFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(pvrFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(pvrFilePath);
                    string fileDirectory = Path.GetDirectoryName(pvrFilePath) ?? string.Empty;
                    fileName = fileName.Replace(".png", "", StringComparison.OrdinalIgnoreCase);

                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    try
                    {
                        bool conversionSuccess = await ConvertPvrToPng(pvrFilePath, pngFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess && File.Exists(pngFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(pngFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngFilePath)}");
                            OnFileConverted(pngFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.pvr转换失败");
                            OnConversionFailed($"{fileName}.pvr转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.pvr处理错误:{ex.Message}");
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

        private async Task<bool> ConvertPvrToPng(string pvrFilePath, string pngFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"-i \"{pvrFilePath}\" -d \"{pngFilePath}\" -noout",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(pvrFilePath)}");
                        return false;
                    }

                    StringBuilder outputBuilder = new StringBuilder();
                    StringBuilder errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            if (e.Data.Contains("Saved decompressed surface") ||
                                e.Data.Contains("successfully") ||
                                e.Data.Contains("completed"))
                            {
                                ConversionProgress?.Invoke(this, $"[PVRTexTool] {e.Data}");
                            }
                            else if (!e.Data.Contains("PVRTexToolCLI") &&
                                     !e.Data.Contains("Copyright") &&
                                     !string.IsNullOrWhiteSpace(e.Data))
                            {
                                ConversionProgress?.Invoke(this, $"[PVRTexTool] {e.Data}");
                            }
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorBuilder.AppendLine(e.Data);
                            if (!e.Data.Contains("Saved decompressed surface") &&
                                !e.Data.Contains("successfully") &&
                                !e.Data.Contains("completed"))
                            {
                                ConversionError?.Invoke(this, $"[PVRTexTool] 错误: {e.Data}");
                            }
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    string output = outputBuilder.ToString();
                    string error = errorBuilder.ToString();

                    if (process.ExitCode == 0)
                    {
                        bool fileExists = File.Exists(pngFilePath);
                        if (fileExists)
                        {
                            if (error.Contains("Saved decompressed surface"))
                            {
                                ConversionProgress?.Invoke(this, $"[PVRTexTool]成功转换:{Path.GetFileName(pngFilePath)}");
                            }
                            return true;
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"转换成功但未生成输出文件:{pngFilePath}");
                            return false;
                        }
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"转换失败，错误代码:{process.ExitCode}");
                        if (!string.IsNullOrEmpty(output))
                            ConversionProgress?.Invoke(this, $"[PVRTexTool]输出:{output}");
                        if (!string.IsNullOrEmpty(error))
                            ConversionError?.Invoke(this, $"[PVRTexTool]错误输出:{error}");

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
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                if (File.Exists(pngFilePath))
                {
                    try { File.Delete(pngFilePath); } catch { }
                }
                return false;
            }
        }
    }
}