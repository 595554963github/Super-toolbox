using System.Diagnostics;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2sd2_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static string _tempExePath;

        static Wav2sd2_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.codec.exe", "codec.exe");
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

            var wavFiles = Directory.GetFiles(directoryPath, "*.wav", SearchOption.AllDirectories)
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

            TotalFilesToConvert = wavFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}");

                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                    string sd2FilePath = Path.Combine(fileDirectory, $"{fileName}.sd2");

                    try
                    {
                        if (File.Exists(sd2FilePath)) File.Delete(sd2FilePath);

                        bool conversionSuccess = await ConvertWavToSd2(wavFilePath, sd2FilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(sd2FilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(sd2FilePath)}");
                            OnFileConverted(sd2FilePath);
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

        private async Task<bool> ConvertWavToSd2(string wavFilePath, string sd2FilePath, CancellationToken cancellationToken)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"-e \"{wavFilePath}\" -f sd2",
                    WorkingDirectory = Path.GetTempPath(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(wavFilePath)}");
                        return false;
                    }

                    await process.WaitForExitAsync(cancellationToken);

                    string expectedOutputPath = Path.Combine(Path.GetDirectoryName(wavFilePath) ?? string.Empty, Path.GetFileNameWithoutExtension(wavFilePath) + ".sd2");
                    return process.ExitCode == 0 && File.Exists(expectedOutputPath);
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