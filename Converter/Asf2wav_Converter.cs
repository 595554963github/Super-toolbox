using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Asf2wav_Converter : BaseExtractor
    {
        private static string? _tempDllPath;
        private static bool _dllLoaded;

        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        static Asf2wav_Converter()
        {
            try
            {
                _tempDllPath = LoadEmbeddedDll("embedded.ea_asf_decoder.dll", "ea_asf_decoder.dll");
                _dllLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载DLL失败:{ex.Message}");
                _dllLoaded = false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private new static string LoadEmbeddedDll(string resourceName, string outputFileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string tempPath = Path.Combine(Path.GetTempPath(), outputFileName);

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException($"嵌入式资源{resourceName}未找到");

                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }

            var loaded = LoadLibrary(tempPath);
            if (loaded == IntPtr.Zero)
                throw new InvalidOperationException($"无法加载DLL:{tempPath}");

            return tempPath;
        }

        [DllImport("ea_asf_decoder.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int DecodeAsfFile(string inputPath, string outputPath);

        [DllImport("ea_asf_decoder.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetAsfInfo(string inputPath, out int revision, out int channels, out int sampleRate, out uint totalSamples);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!_dllLoaded)
            {
                ConversionError?.Invoke(this, "无法加载ea_asf_decoder.dll");
                OnConversionFailed("无法加载ea_asf_decoder.dll");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var asfFiles = Directory.GetFiles(directoryPath, "*.asf", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"\d+");
                    if (match.Success && int.TryParse(match.Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = asfFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var asfFilePath in asfFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(asfFilePath);
                    ConversionProgress?.Invoke(this, $"正在转换:{Path.GetFileName(asfFilePath)}");

                    string fileDirectory = Path.GetDirectoryName(asfFilePath) ?? string.Empty;
                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        GetAsfInfo(asfFilePath, out int rev, out _, out _, out _);
                        ConversionProgress?.Invoke(this, $"  ASF版本:R{rev}");

                        if (File.Exists(wavFilePath))
                            File.Delete(wavFilePath);

                        bool conversionSuccess = await Task.Run(() => DecodeAsfFile(asfFilePath, wavFilePath) == 0 && File.Exists(wavFilePath), cancellationToken);

                        if (conversionSuccess)
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"已转换:{Path.GetFileName(wavFilePath)}");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{Path.GetFileName(asfFilePath)}转换失败");
                            OnConversionFailed($"{Path.GetFileName(asfFilePath)}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}处理错误:{ex.Message}");
                    }
                }

                ConversionProgress?.Invoke(this, successCount > 0
                    ? $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件"
                    : "转换完成,但未成功转换任何文件");

                OnConversionCompleted();
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
    }
}