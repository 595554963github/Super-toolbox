using System.Diagnostics;

namespace super_toolbox
{
    public class MTXT2DDS_Converter : BaseExtractor
    {
        private static string _tempExePath;

        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        static MTXT2DDS_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.xenoTextureConvert.exe", "xenoTextureConvert.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            if (!File.Exists(_tempExePath))
            {
                ConversionError?.Invoke(this, "转换工具提取失败");
                OnConversionFailed("转换工具提取失败");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var mtxtFiles = new List<string>();
            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);

            foreach (var file in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsMtxtFile(file))
                {
                    mtxtFiles.Add(file);
                }
            }

            TotalFilesToConvert = mtxtFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var mtxtFilePath in mtxtFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fileName = Path.GetFileNameWithoutExtension(mtxtFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}");

                    string fileDirectory = Path.GetDirectoryName(mtxtFilePath) ?? string.Empty;
                    string ddsFile = Path.Combine(fileDirectory, $"{fileName}.dds");

                    try
                    {
                        if (File.Exists(ddsFile))
                            File.Delete(ddsFile);

                        bool conversionSuccess = await ConvertMtxtToDds(mtxtFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess && File.Exists(ddsFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(ddsFile)}");
                            OnFileConverted(ddsFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}转换失败或未生成输出文件");
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

        private bool IsMtxtFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 4) return false;

                    fs.Seek(-4, SeekOrigin.End);
                    byte[] buffer = new byte[4];
                    fs.Read(buffer, 0, 4);

                    return buffer[0] == 0x4D && buffer[1] == 0x54 &&
                           buffer[2] == 0x58 && buffer[3] == 0x54; 
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> ConvertMtxtToDds(string mtxtFilePath, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"\"{mtxtFilePath}\"",
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
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(mtxtFilePath)}");
                        return false;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionProgress?.Invoke(this, $"[xenoTextureConvert] {e.Data}");
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionError?.Invoke(this, $"[xenoTextureConvert]错误:{e.Data}");
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