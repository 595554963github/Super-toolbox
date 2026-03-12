using System.Diagnostics;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Xma2wav4_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static string _tempExePath;

        static Xma2wav4_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.AdpcmEncode.exe", "AdpcmEncode.exe");
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

            var xmaFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => IsMsadpcmFile(f))
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = xmaFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var xmaFilePath in xmaFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(xmaFilePath);
                    string fileExt = Path.GetExtension(xmaFilePath);
                    string fileDirectory = Path.GetDirectoryName(xmaFilePath) ?? string.Empty;

                    string wavFilePath;
                    if (fileExt.Equals(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        wavFilePath = Path.Combine(fileDirectory, $"{fileName}_.wav");
                    }
                    else
                    {
                        wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");
                    }

                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}{fileExt}");

                    try
                    {
                        if (File.Exists(wavFilePath))
                            File.Delete(wavFilePath);

                        bool conversionSuccess = await ConvertToWAV(xmaFilePath, wavFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(wavFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFilePath)}");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}{fileExt}转换失败");
                            OnConversionFailed($"{fileName}{fileExt}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}{fileExt}处理错误:{ex.Message}");
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

        private bool IsMsadpcmFile(string filePath)
        {
            try
            {
                byte[] buffer = new byte[6];
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 0x16)
                        return false;
                    fs.Seek(0x10, SeekOrigin.Begin);
                    int bytesRead = fs.Read(buffer, 0, 6);
                    if (bytesRead < 6)
                        return false;
                }

                return buffer[0] == 0x32 && buffer[1] == 0x00 && buffer[2] == 0x00 &&
                       buffer[3] == 0x00 && buffer[4] == 0x02 && buffer[5] == 0x00;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> ConvertToWAV(string xmaFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"\"{xmaFilePath}\" \"{wavFilePath}\"",
                    WorkingDirectory = Path.GetTempPath(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(xmaFilePath)}");
                        return false;
                    }

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionProgress?.Invoke(this, $"[AdpcmEncode] {e.Data}");
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            ConversionError?.Invoke(this, $"[AdpcmEncode]错误:{e.Data}");
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