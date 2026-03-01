using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class At92wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static string? _tempExePath;
        private static bool _exeExtracted = false;
        private static readonly object _lock = new object();

        static At92wav_Converter()
        {
            ExtractEmbeddedExe();
        }

        private static void ExtractEmbeddedExe()
        {
            if (_exeExtracted) return;

            lock (_lock)
            {
                if (_exeExtracted) return;

                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
                    Directory.CreateDirectory(tempDir);
                    _tempExePath = Path.Combine(tempDir, "at9tool.exe");

                    if (!File.Exists(_tempExePath))
                    {
                        Assembly assembly = Assembly.GetExecutingAssembly();
                        string resourceName = "embedded.at9tool.exe";

                        using (var stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream == null)
                                throw new FileNotFoundException($"嵌入的at9tool资源未找到:{resourceName}");

                            byte[] buffer = new byte[stream.Length];
                            stream.Read(buffer, 0, buffer.Length);
                            File.WriteAllBytes(_tempExePath, buffer);
                        }
                    }

                    _exeExtracted = true;
                }
                catch (Exception ex)
                {
                    throw new Exception($"提取at9tool.exe失败:{ex.Message}");
                }
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var at9Files = Directory.GetFiles(directoryPath, "*.at9", SearchOption.AllDirectories)
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

                    TotalFilesToConvert = at9Files.Length;
                    int successCount = 0;

                    if (at9Files.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的AT9文件");
                        OnConversionFailed("未找到需要转换的AT9文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个AT9文件");

                    foreach (var at9FilePath in at9Files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(at9FilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.at9");

                        string fileDirectory = Path.GetDirectoryName(at9FilePath) ?? string.Empty;
                        string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                        try
                        {
                            if (File.Exists(wavFilePath))
                                File.Delete(wavFilePath);

                            if (ConvertAt9ToWav(at9FilePath, wavFilePath) && File.Exists(wavFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav");
                                OnFileConverted(wavFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.at9转换失败");
                                OnConversionFailed($"{fileName}.at9转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.at9处理错误:{ex.Message}");
                        }
                    }

                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "转换操作已取消");
                OnConversionFailed("转换操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换失败:{ex.Message}");
                OnConversionFailed($"转换失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        private bool ConvertAt9ToWav(string at9FilePath, string wavFilePath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"-d \"{at9FilePath}\" \"{wavFilePath}\"",
                        WorkingDirectory = Path.GetTempPath(),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    ConversionError?.Invoke(this, $"at9tool错误:{error}");
                    return false;
                }

                return File.Exists(wavFilePath);
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return false;
            }
        }
    }
}