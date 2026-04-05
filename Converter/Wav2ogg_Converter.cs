using System.Diagnostics;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2ogg_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static string _tempExePath;

        static Wav2ogg_Converter()
        {
            _tempExePath = LoadEmbeddedExe("embedded.oggenc2.exe", "oggenc2.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

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

            if (wavFiles.Length == 0)
            {
                ConversionError?.Invoke(this, "未找到WAV文件");
                OnConversionFailed("未找到WAV文件");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个WAV文件");

            foreach (var wavFile in wavFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);

                string fileName = Path.GetFileNameWithoutExtension(wavFile);
                ConversionProgress?.Invoke(this, $"正在转换:{fileName}.wav");

                string oggFile = Path.ChangeExtension(wavFile, ".ogg");

                try
                {
                    if (File.Exists(oggFile)) File.Delete(oggFile);

                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"\"{wavFile}\" -Q",
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
                            ConversionError?.Invoke(this, $"{fileName}.wav转换失败:无法启动进程");
                            OnConversionFailed($"{fileName}.wav转换失败");
                            continue;
                        }

                        await process.WaitForExitAsync(cancellationToken);

                        if (process.ExitCode == 0 && File.Exists(oggFile) && new FileInfo(oggFile).Length > 0)
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"已转换:{fileName}.ogg");
                            OnFileConverted(oggFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.wav转换失败");
                            OnConversionFailed($"{fileName}.wav转换失败");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConversionError?.Invoke(this, $"{fileName}.wav转换异常:{ex.Message}");
                    OnConversionFailed($"{fileName}.wav处理错误:{ex.Message}");
                }
            }

            ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
            OnConversionCompleted();
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}