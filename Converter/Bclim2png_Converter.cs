using System.Diagnostics;

namespace super_toolbox
{
    public class Bclim2png_Converter : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;

        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        static Bclim2png_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.png2bclim.exe", "png2bclim.exe");
            _tempDllPath = LoadEmbeddedExe("embedded.ETC1Lib.dll", "ETC1Lib.dll");
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

            if (!File.Exists(_tempExePath) || !File.Exists(_tempDllPath))
            {
                ConversionError?.Invoke(this, "转换工具提取失败，无法继续");
                OnConversionFailed("转换工具提取失败");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var bclimFiles = new List<string>();
            bclimFiles.AddRange(Directory.EnumerateFiles(directoryPath, "*.bclim", SearchOption.AllDirectories));

            TotalFilesToConvert = bclimFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var bclimFilePath in bclimFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    if (!IsValidBclimFile(bclimFilePath))
                    {
                        ConversionProgress?.Invoke(this, $"跳过非BCLIM文件:{Path.GetFileName(bclimFilePath)}");
                        continue;
                    }

                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(bclimFilePath)}");

                    string fileDirectory = Path.GetDirectoryName(bclimFilePath) ?? string.Empty;
                    string expectedPngPath = Path.Combine(fileDirectory, $"{Path.GetFileNameWithoutExtension(bclimFilePath)}.png");

                    try
                    {
                        bool conversionSuccess = await ConvertBclimToPngUsingExeAsync(bclimFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess && File.Exists(expectedPngPath))
                        {
                            successCount++;
                            convertedFiles.Add(expectedPngPath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(expectedPngPath)}");
                            OnFileConverted(expectedPngPath);
                        }
                        else
                        {
                            var pngFilesBefore = GetExistingPngFiles(fileDirectory);

                            bool toolSuccess = await RunPng2BclimToolAsync(bclimFilePath, fileDirectory, cancellationToken);

                            if (toolSuccess)
                            {
                                var pngFilesAfter = GetExistingPngFiles(fileDirectory);
                                var newPngFiles = pngFilesAfter.Except(pngFilesBefore).ToList();

                                if (newPngFiles.Any())
                                {
                                    string newPngFile = newPngFiles.First();
                                    successCount++;
                                    convertedFiles.Add(newPngFile);
                                    ConversionProgress?.Invoke(this, $"转换成功(检测到新文件):{Path.GetFileName(newPngFile)}");
                                    OnFileConverted(newPngFile);
                                }
                                else
                                {
                                    ConversionError?.Invoke(this, $"{Path.GetFileName(bclimFilePath)} 转换成功但未生成PNG文件");
                                    OnConversionFailed($"{Path.GetFileName(bclimFilePath)} 转换失败");
                                }
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{Path.GetFileName(bclimFilePath)}转换失败");
                                OnConversionFailed($"{Path.GetFileName(bclimFilePath)}转换失败");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{Path.GetFileName(bclimFilePath)} 处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成，成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成，但未成功转换任何文件");
                    OnConversionCompleted();
                }
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

        private async Task<bool> ConvertBclimToPngUsingExeAsync(string bclimFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            return await RunPng2BclimToolAsync(bclimFilePath, workingDirectory, cancellationToken);
        }

        private async Task<bool> RunPng2BclimToolAsync(string bclimFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"调用png2bclim转换:{Path.GetFileName(bclimFilePath)}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"\"{bclimFilePath}\"",
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
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(bclimFilePath)}");
                        return false;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionProgress?.Invoke(this, $"[png2bclim]{e.Data}");
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionError?.Invoke(this, $"[png2bclim]错误:{e.Data}");
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0)
                    {
                        ConversionProgress?.Invoke(this, $"png2bclim处理完成:{Path.GetFileName(bclimFilePath)}");
                        return true;
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"转换失败，错误代码:{process.ExitCode}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return false;
            }
        }

        private List<string> GetExistingPngFiles(string directory)
        {
            try
            {
                return Directory.GetFiles(directory, "*.png").ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private bool IsValidBclimFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 0x28)
                        return false;

                    fs.Seek(-0x28, SeekOrigin.End);
                    byte[] signature = br.ReadBytes(4);
                    return System.Text.Encoding.ASCII.GetString(signature) == "CLIM";
                }
            }
            catch
            {
                return false;
            }
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}