using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2lopus_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static string? _tempDllPath;
        private static bool _dllExtracted = false;
        private static readonly object _lock = new object();
        private IntPtr _dllHandle = IntPtr.Zero;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int EncodeWavToLopusDelegate(string inputPath, string outputPath, StringBuilder errorMsg, int errorMsgSize);

        private EncodeWavToLopusDelegate? _encodeFunc;

        static Wav2lopus_Converter()
        {
            ExtractEmbeddedDll();
        }

        private static void ExtractEmbeddedDll()
        {
            if (_dllExtracted) return;

            lock (_lock)
            {
                if (_dllExtracted) return;

                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
                    Directory.CreateDirectory(tempDir);
                    _tempDllPath = Path.Combine(tempDir, "lopus_tools.dll");

                    if (!File.Exists(_tempDllPath))
                    {
                        Assembly assembly = Assembly.GetExecutingAssembly();
                        string resourceName = "embedded.lopus_tools.dll";

                        using (var stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream == null)
                                throw new FileNotFoundException($"嵌入的lopus_tools.dll资源未找到:{resourceName}");

                            byte[] buffer = new byte[stream.Length];
                            stream.Read(buffer, 0, buffer.Length);
                            File.WriteAllBytes(_tempDllPath, buffer);
                        }
                    }

                    _dllExtracted = true;
                }
                catch (Exception ex)
                {
                    throw new Exception($"提取lopus_tools.dll失败:{ex.Message}");
                }
            }
        }

        private void LoadDllFunctions()
        {
            if (_dllHandle != IntPtr.Zero) return;

            try
            {
                if (string.IsNullOrEmpty(_tempDllPath))
                    throw new InvalidOperationException("DLL路径未初始化");
                _dllHandle = NativeLibrary.Load(_tempDllPath);
                IntPtr funcPtr = NativeLibrary.GetExport(_dllHandle, "EncodeWavToLopus");
                _encodeFunc = Marshal.GetDelegateForFunctionPointer<EncodeWavToLopusDelegate>(funcPtr);
            }
            catch (Exception ex)
            {
                throw new Exception($"加载DLL函数失败:{ex.Message}");
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
                    LoadDllFunctions();

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
                        ConversionError?.Invoke(this, "未找到需要转换的WAV文件");
                        OnConversionFailed("未找到需要转换的WAV文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个WAV文件");

                    foreach (var wavFilePath in wavFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.wav");

                        string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                        string lopusFilePath = Path.Combine(fileDirectory, $"{fileName}.lopus");

                        try
                        {
                            StringBuilder errorMsg = new StringBuilder(256);
                            int result = _encodeFunc!(wavFilePath, lopusFilePath, errorMsg, errorMsg.Capacity);

                            if (result == 0 && File.Exists(lopusFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.lopus");
                                OnFileConverted(lopusFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.wav转换失败:{errorMsg}");
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
    }
}