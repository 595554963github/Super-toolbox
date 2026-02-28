using System.Diagnostics;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2opus_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static string _tempExePath;

        static Wav2opus_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.opusenc.exe", "opusenc.exe");
        }

        public int Bitrate { get; set; } = 64;

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
                    string opusFilePath = Path.Combine(fileDirectory, $"{fileName}.opus");

                    try
                    {
                        var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"--bitrate {Bitrate} \"{wavFilePath}\" \"{opusFilePath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true
                        });

                        if (process == null)
                        {
                            ConversionError?.Invoke(this, $"无法启动转换进程:{Path.GetFileName(wavFilePath)}");
                            continue;
                        }

                        string error = await process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync(cancellationToken);

                        if (process.ExitCode == 0 && File.Exists(opusFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{fileName}.opus");
                            OnFileConverted(opusFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.wav转换失败:{error}");
                            OnConversionFailed($"{fileName}.wav转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.wav处理错误:{ex.Message}");
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