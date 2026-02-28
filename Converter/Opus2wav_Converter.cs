using System.Diagnostics;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Opus2wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static string _tempExePath;

        static Opus2wav_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.opusdec.exe", "opusdec.exe");
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

            var opusFiles = Directory.GetFiles(directoryPath, "*.opus", SearchOption.AllDirectories)
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

            TotalFilesToConvert = opusFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var opusFilePath in opusFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(opusFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.opus");

                    string fileDirectory = Path.GetDirectoryName(opusFilePath) ?? string.Empty;
                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"\"{opusFilePath}\" \"{wavFilePath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true
                        });

                        if (process == null)
                        {
                            ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(opusFilePath)}");
                            continue;
                        }

                        string error = await process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync(cancellationToken);

                        if (process.ExitCode == 0 && File.Exists(wavFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{fileName}.wav");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.opus转换失败:{error}");
                            OnConversionFailed($"{fileName}.opus转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.opus处理错误:{ex.Message}");
                    }
                }

                ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
        }
    }
}