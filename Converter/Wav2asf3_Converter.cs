using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;
using VGAudio.Formats.Pcm8;

namespace super_toolbox
{
    public class Wav2asf3_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static string _tempDllPath;

        static Wav2asf3_Converter()
        {
            _tempDllPath = LoadEmbeddedDll("embedded.EA_XA-ADPCM.dll", "EA_XA-ADPCM_R3.dll");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private delegate int ConvertWavToAsfDelegate(string inputPath, string outputPath);

        private new static string LoadEmbeddedDll(string resourceName, string outputFileName)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), outputFileName);

            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch
                {
                    tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(outputFileName)}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.dll");
                }
            }

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new Exception($"嵌入式资源{resourceName}未找到");

                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }

            return tempPath;
        }

        private int CallDllFunction(string wavFilePath, string asfFilePath)
        {
            IntPtr hModule = IntPtr.Zero;
            try
            {
                hModule = LoadLibrary(_tempDllPath);
                if (hModule == IntPtr.Zero)
                    throw new Exception($"无法加载DLL,错误代码:{Marshal.GetLastWin32Error()}");

                IntPtr pFunc = GetProcAddress(hModule, "ConvertWavToAsfR3");
                if (pFunc == IntPtr.Zero)
                    throw new Exception("找不到函数ConvertWavToAsfR3");

                ConvertWavToAsfDelegate convertFunc = Marshal.GetDelegateForFunctionPointer<ConvertWavToAsfDelegate>(pFunc);
                return convertFunc(wavFilePath, asfFilePath);
            }
            finally
            {
                if (hModule != IntPtr.Zero) FreeLibrary(hModule);
            }
        }

        private async Task<bool> ConvertToPcmWav(string inputWav, string tempPcmWav, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取WAV文件:{Path.GetFileName(inputWav)}");

                var wavReader = new WaveReader();
                AudioData audioData;

                using (var wavStream = File.OpenRead(inputWav))
                {
                    audioData = wavReader.Read(wavStream);
                }

                if (audioData == null)
                {
                    ConversionError?.Invoke(this, "无法读取WAV音频数据");
                    return false;
                }

                var pcm16 = audioData.GetFormat<Pcm16Format>();
                if (pcm16 == null)
                {
                    var pcm8 = audioData.GetFormat<Pcm8Format>();
                    if (pcm8 != null)
                    {
                        pcm16 = pcm8.ToPcm16();
                    }
                    else
                    {
                        ConversionError?.Invoke(this, "不支持的WAV格式(仅支持PCM8/PCM16)");
                        return false;
                    }
                }

                var wavWriter = new WaveWriter();
                using (var outputStream = File.Create(tempPcmWav))
                {
                    wavWriter.WriteToStream(pcm16, outputStream);
                }

                return true;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }

        private async Task<bool> ConvertWAVToASF(string wavFilePath, string asfFilePath, CancellationToken cancellationToken)
        {
            string tempPcmWav = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_pcm.wav");

            try
            {
                bool pcmSuccess = await ConvertToPcmWav(wavFilePath, tempPcmWav, cancellationToken);
                if (!pcmSuccess) return false;

                return await Task.Run(() =>
                {
                    int result = CallDllFunction(tempPcmWav, asfFilePath);

                    if (result != 0)
                    {
                        ConversionError?.Invoke(this, $"转换失败,错误码:{result}");
                        return false;
                    }

                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                return false;
            }
            finally
            {
                try { if (File.Exists(tempPcmWav)) File.Delete(tempPcmWav); }
                catch { }
            }
        }

        public async Task ExtractSingleAsync(string wavFilePath, string outputPath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(wavFilePath))
            {
                ConversionError?.Invoke(this, $"源文件{wavFilePath}不存在");
                OnConversionFailed($"源文件{wavFilePath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理:{Path.GetFileName(wavFilePath)}");

            try
            {
                bool success = await ConvertWAVToASF(wavFilePath, outputPath, cancellationToken);

                if (success && File.Exists(outputPath))
                {
                    ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(outputPath)}");
                    OnFileConverted(outputPath);
                    OnConversionCompleted();
                }
                else
                {
                    ConversionError?.Invoke(this, $"{Path.GetFileName(wavFilePath)}转换失败");
                    OnConversionFailed($"{Path.GetFileName(wavFilePath)}转换失败");
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                OnConversionFailed($"{Path.GetFileName(wavFilePath)}处理错误:{ex.Message}");
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

            var wavFiles = Directory.GetFiles(directoryPath, "*.wav", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    return match.Success && int.TryParse(match.Groups[1].Value, out int num) ? num : int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = wavFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.wav");

                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                    string asfFilePath = Path.Combine(fileDirectory, $"{fileName}.asf");

                    try
                    {
                        bool conversionSuccess = await ConvertWAVToASF(wavFilePath, asfFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(asfFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(asfFilePath)}");
                            OnFileConverted(asfFilePath);
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

                ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
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
    }
}