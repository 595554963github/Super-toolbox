using System.Diagnostics;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2ast_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static string _tempExePath;

        static Wav2ast_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.ast_codec.exe", "ast_codec.exe");
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
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.wav");

                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                    string astFile = Path.Combine(fileDirectory, $"{fileName}.ast");

                    try
                    {
                        if (File.Exists(astFile))
                        {
                            File.Delete(astFile);
                        }

                        var processStartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"-e \"{wavFilePath}\"",
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
                                ConversionError?.Invoke(this, $"{fileName}.wav转换失败:无法启动进程");
                                OnConversionFailed($"{fileName}.wav转换失败");
                                continue;
                            }

                            await process.WaitForExitAsync(cancellationToken);

                            string error = await process.StandardError.ReadToEndAsync();

                            if (process.ExitCode == 0 && File.Exists(astFile) && new FileInfo(astFile).Length > 0)
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(astFile)}");
                                OnFileConverted(astFile);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.wav转换失败:{error}");
                                OnConversionFailed($"{fileName}.wav转换失败");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.wav处理错误:{ex.Message}");
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

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}