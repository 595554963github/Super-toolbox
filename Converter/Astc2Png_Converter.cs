using System.Diagnostics;

namespace super_toolbox
{
    public class Astc2Png_Converter : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static readonly string[] CommonBlockSizes = new[]
        {
            "4x4", "5x4", "5x5", "6x5", "6x6", "8x5", "8x6", "8x8", "10x5", "10x6",
            "10x8", "10x10", "12x10", "12x12"
        };

        static Astc2Png_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.astcenc-avx2.exe", "astcenc-avx2.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> convertedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹 {directoryPath} 不存在");
                OnConversionFailed($"源文件夹 {directoryPath} 不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            TotalFilesToConvert = Directory.GetFiles(directoryPath, "*.astc", SearchOption.AllDirectories).Length;

            var astcFiles = Directory.EnumerateFiles(directoryPath, "*.astc", SearchOption.AllDirectories);
            int successCount = 0;

            try
            {
                foreach (var astcFilePath in astcFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(astcFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(astcFilePath);
                    string fileDirectory = Path.GetDirectoryName(astcFilePath) ?? string.Empty;

                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    try
                    {
                        bool conversionSuccess = await TryConvertAstcToPng(astcFilePath, pngFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess && File.Exists(pngFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(pngFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngFilePath)}");
                            OnFileConverted(pngFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.astc转换失败");
                            OnConversionFailed($"{fileName}.astc转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.astc处理错误:{ex.Message}");
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
                ConversionError?.Invoke(this, $"严重错误: {ex.Message}");
                OnConversionFailed($"严重错误: {ex.Message}");
            }
        }

        private async Task<bool> TryConvertAstcToPng(string astcFilePath, string pngFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            ConversionProgress?.Invoke(this, "尝试使用-dl自动检测ASTC格式...");

            bool success = await RunAstcEncoder(astcFilePath, pngFilePath, workingDirectory, "-dl", "", cancellationToken);
            if (success)
            {
                ConversionProgress?.Invoke(this, "使用-dl自动检测成功");
                return true;
            }

            ConversionProgress?.Invoke(this, "尝试使用-ds自动检测ASTC格式...");

            success = await RunAstcEncoder(astcFilePath, pngFilePath, workingDirectory, "-ds", "", cancellationToken);
            if (success)
            {
                ConversionProgress?.Invoke(this, "使用-ds自动检测成功");
                return true;
            }

            ConversionProgress?.Invoke(this, "自动检测失败，尝试常见块尺寸...");

            foreach (string blockSize in CommonBlockSizes)
            {
                ConversionProgress?.Invoke(this, $"尝试块尺寸: {blockSize}");

                success = await RunAstcEncoder(astcFilePath, pngFilePath, workingDirectory, "-ds", blockSize, cancellationToken);
                if (success)
                {
                    ConversionProgress?.Invoke(this, $"使用块尺寸{blockSize}转换成功");
                    return true;
                }

                if (cancellationToken.IsCancellationRequested)
                    break;
            }

            ConversionError?.Invoke(this, "所有解码尝试均失败");
            return false;
        }

        private async Task<bool> RunAstcEncoder(string inputFile, string outputFile, string workingDirectory, string decodeMode, string blockSize, CancellationToken cancellationToken)
        {
            try
            {
                string arguments;
                if (string.IsNullOrEmpty(blockSize))
                {
                    arguments = $"{decodeMode} \"{inputFile}\" \"{outputFile}\"";
                }
                else
                {
                    arguments = $"{decodeMode} \"{inputFile}\" \"{outputFile}\" {blockSize}";
                }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = arguments,
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
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(inputFile)}");
                        return false;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionProgress?.Invoke(this, $"[{decodeMode}] {e.Data}");
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionError?.Invoke(this, $"[{decodeMode}]错误:{e.Data}");
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0)
                    {
                        return File.Exists(outputFile);
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"转换失败，错误代码:{process.ExitCode} (模式:{decodeMode}, 块尺寸:{blockSize ?? "自动"})");
                        if (File.Exists(outputFile))
                        {
                            try { File.Delete(outputFile); } catch { }
                        }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"[{decodeMode}]转换过程异常: {ex.Message}");
                if (File.Exists(outputFile))
                {
                    try { File.Delete(outputFile); } catch { }
                }
                return false;
            }
        }
    }
}