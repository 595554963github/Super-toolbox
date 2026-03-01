using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Opus2wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        [DllImport("opus_tool.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int DecodeOpusToWav(string inputPath, string outputPath, StringBuilder errorMsg, int errorMsgSize);

        private static string? _tempDllPath;
        private static bool _dllExtracted = false;
        private static readonly object _lock = new object();

        static Opus2wav_Converter()
        {
            EnsureDllExtracted();
        }

        private static void EnsureDllExtracted()
        {
            if (_dllExtracted) return;

            lock (_lock)
            {
                if (_dllExtracted) return;

                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
                    Directory.CreateDirectory(tempDir);
                    _tempDllPath = Path.Combine(tempDir, "opus_tool.dll");

                    if (!File.Exists(_tempDllPath))
                    {
                        var assembly = typeof(Wav2opus_Converter).Assembly;
                        string[] resourceNames = assembly.GetManifestResourceNames();
                        string? resourceName = resourceNames.FirstOrDefault(n => n.EndsWith("opus_tool.dll"));

                        if (string.IsNullOrEmpty(resourceName))
                            throw new Exception("未找到嵌入的opus_tool.dll");

                        using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream == null)
                                throw new Exception("加载嵌入的dll流失败");

                            byte[] buffer = new byte[stream.Length];
                            stream.Read(buffer, 0, buffer.Length);
                            File.WriteAllBytes(_tempDllPath, buffer);
                        }
                    }

                    _dllExtracted = true;
                }
                catch (Exception ex)
                {
                    throw new Exception($"提取dll失败:{ex.Message}");
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
                        StringBuilder errorMsg = new StringBuilder(256);
                        int result = DecodeOpusToWav(opusFilePath, wavFilePath, errorMsg, errorMsg.Capacity);

                        if (result == 0 && File.Exists(wavFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{fileName}.wav");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.opus转换失败:{errorMsg}");
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
