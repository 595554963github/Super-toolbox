using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2bcwav1_Converter : BaseExtractor
    {
        private const int CWAV_ENCODING_PCM8 = 0;
        private static string? _tempDllPath;
        private static bool _dllLoaded;

        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        static Wav2bcwav1_Converter()
        {
            try
            {
                _tempDllPath = LoadEmbeddedDll("embedded.bcwavtool.dll", "bcwavtool_pcm8.dll");
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

            LoadLibrary(tempPath);
            return tempPath;
        }

        [DllImport("bcwavtool_pcm8.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int BCWAV_Encode(
            string inputFile,
            string outputFile,
            int encoding,
            bool loop,
            uint loopStart,
            uint loopEnd);

        [DllImport("bcwavtool_pcm8.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void BCWAV_GetLastError(IntPtr buffer, int bufferSize);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!_dllLoaded)
            {
                ConversionError?.Invoke(this, "无法加载bcwavtool.dll");
                OnConversionFailed("无法加载bcwavtool.dll");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath} (PCM8编码)");

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
                    string bcwavFile = Path.Combine(fileDirectory, $"{fileName}.bcwav");

                    try
                    {
                        if (File.Exists(bcwavFile))
                            File.Delete(bcwavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToBcwav(wavFilePath, bcwavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(bcwavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(bcwavFile)} (PCM8)");
                            OnFileConverted(bcwavFile);
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

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件(PCM8)");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件(PCM8)");
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

        private bool ConvertWavToBcwav(string wavFilePath, string bcwavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取wav文件:{Path.GetFileName(wavFilePath)}");

                int result = BCWAV_Encode(wavFilePath, bcwavFilePath, CWAV_ENCODING_PCM8, false, 0, 0);

                if (result != 0)
                {
                    string errorMsg = GetLastError();
                    throw new Exception($"编码失败:{errorMsg}");
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }

        private string GetLastError()
        {
            IntPtr ptr = Marshal.AllocHGlobal(512);
            try
            {
                BCWAV_GetLastError(ptr, 512);
                return Marshal.PtrToStringAnsi(ptr) ?? "未知错误";
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}