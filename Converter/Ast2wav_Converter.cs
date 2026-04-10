using System.Diagnostics;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Ast2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static string _tempExePath;

        static Ast2wav_Converter()
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

            ConversionStarted?.Invoke(this, $"开始解码目录:{directoryPath}");

            var astFiles = Directory.GetFiles(directoryPath, "*.ast", SearchOption.AllDirectories)
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

            TotalFilesToConvert = astFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var astFilePath in astFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(astFilePath);
                    ConversionProgress?.Invoke(this, $"正在解码:{fileName}.ast");

                    string fileDirectory = Path.GetDirectoryName(astFilePath) ?? string.Empty;
                    string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavFile))
                        {
                            File.Delete(wavFile);
                        }

                        var processStartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"-d \"{astFilePath}\"",
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
                                ConversionError?.Invoke(this, $"{fileName}.ast解码失败:无法启动进程");
                                OnConversionFailed($"{fileName}.ast解码失败");
                                continue;
                            }

                            await process.WaitForExitAsync(cancellationToken);

                            string error = await process.StandardError.ReadToEndAsync();

                            if (process.ExitCode == 0 && File.Exists(wavFile) && new FileInfo(wavFile).Length > 0)
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"解码成功:{Path.GetFileName(wavFile)}");
                                OnFileConverted(wavFile);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.ast解码失败:{error}");
                                OnConversionFailed($"{fileName}.ast解码失败");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"解码异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.ast处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"解码完成,成功解码{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "解码完成,但未成功解码任何文件");
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