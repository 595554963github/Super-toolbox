using System.Diagnostics;

namespace super_toolbox
{
    public class DXBC2HLSL_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;
        private static readonly byte[] DXBC_MAGIC_HEADER = new byte[] { 0x44, 0x58, 0x42, 0x43 };

        private static string _tempExePath;
        static DXBC2HLSL_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.CMD_Decompiler.exe", "CMD_Decompiler.exe");
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

            var dxbcFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => IsDXBCFile(f))
                .ToArray();

            TotalFilesToConvert = dxbcFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var dxbcFilePath in dxbcFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(dxbcFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}");

                    string fileDirectory = Path.GetDirectoryName(dxbcFilePath) ?? string.Empty;

                    try
                    {
                        bool conversionSuccess = await ConvertDXBCToHLSL(dxbcFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess)
                        {
                            string hlslFile = Path.Combine(fileDirectory, $"{fileName}.hlsl");
                            if (File.Exists(hlslFile))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(hlslFile)}");
                                OnFileConverted(hlslFile);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}转换成功但未找到输出文件");
                                OnConversionFailed($"{fileName}转换成功但未找到输出文件");
                            }
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}转换失败");
                            OnConversionFailed($"{fileName}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}处理错误:{ex.Message}");
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

        private bool IsDXBCFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < DXBC_MAGIC_HEADER.Length)
                        return false;

                    byte[] headerBytes = new byte[DXBC_MAGIC_HEADER.Length];
                    int bytesRead = fs.Read(headerBytes, 0, DXBC_MAGIC_HEADER.Length);

                    if (bytesRead < DXBC_MAGIC_HEADER.Length)
                        return false;

                    return headerBytes.SequenceEqual(DXBC_MAGIC_HEADER);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<bool> ConvertDXBCToHLSL(string dxbcFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"-D \"{dxbcFilePath}\"",
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
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(dxbcFilePath)}");
                        return false;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionProgress?.Invoke(this, $"[CMD_Decompiler] {e.Data}");
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionError?.Invoke(this, $"[CMD_Decompiler]错误:{e.Data}");
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