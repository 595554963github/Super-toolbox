using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2ape_Converter : BaseExtractor
    {
        private static string? _tempExePath;
        private static bool _exeExtracted;

        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public int CompressionLevel { get; set; } = 2000;

        static Wav2ape_Converter()
        {
            try
            {
                _tempExePath = ExtractEmbeddedExe("embedded.mac.exe", "mac.exe");
                _exeExtracted = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"提取EXE失败:{ex.Message}");
                _exeExtracted = false;
            }
        }

        private static string ExtractEmbeddedExe(string resourceName, string outputFileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string tempPath = Path.Combine(Path.GetTempPath(), outputFileName);

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                if (stream == null)
                    throw new InvalidOperationException($"嵌入式资源{resourceName}未找到");
                stream.CopyTo(fileStream);
            }

            return tempPath;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!_exeExtracted)
            {
                ConversionError?.Invoke(this, "无法提取mac.exe");
                OnConversionFailed("无法提取mac.exe");
                return;
            }

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
                    var match = Regex.Match(Path.GetFileNameWithoutExtension(f), @"_(\d+)$");
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
                    string apeFile = Path.Combine(fileDirectory, $"{fileName}.ape");

                    try
                    {
                        if (File.Exists(apeFile))
                            File.Delete(apeFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToApe(wavFilePath, apeFile, cancellationToken));

                        if (conversionSuccess && File.Exists(apeFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(apeFile)}");
                            OnFileConverted(apeFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.wav转换失败");
                            OnConversionFailed($"{fileName}.wav转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.wav处理错误:{ex.Message}");
                    }
                }

                ConversionProgress?.Invoke(this, successCount > 0
                    ? $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件"
                    : "转换完成,但未成功转换任何文件");

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

        private bool ConvertWavToApe(string wavFilePath, string apeFilePath, CancellationToken cancellationToken)
        {
            try
            {
                string arguments = $"\"{wavFilePath}\" \"{apeFilePath}\" -c{CompressionLevel}";
                ConversionProgress?.Invoke(this, $"执行APE编码:{Path.GetFileName(wavFilePath)}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                        throw new Exception($"mac.exe错误:{error}");

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }
    }
}