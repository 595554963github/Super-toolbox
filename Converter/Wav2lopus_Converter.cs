using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;
using VGAudio.Containers.Wave;

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

        private short[][] ResampleSamples(short[][] channels, double ratio)
        {
            int newLength = (int)(channels[0].Length * ratio);
            short[][] result = new short[channels.Length][];

            for (int ch = 0; ch < channels.Length; ch++)
            {
                result[ch] = new short[newLength];

                for (int i = 0; i < newLength; i++)
                {
                    double srcPos = i / ratio;
                    int srcIdx = (int)srcPos;
                    double frac = srcPos - srcIdx;

                    if (srcIdx + 1 >= channels[ch].Length)
                    {
                        result[ch][i] = channels[ch][srcIdx];
                    }
                    else
                    {
                        double sample = channels[ch][srcIdx] * (1.0 - frac) + channels[ch][srcIdx + 1] * frac;
                        result[ch][i] = (short)Math.Round(sample);
                    }
                }
            }

            return result;
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
                    List<string> tempFiles = new List<string>();

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
                        string processedWavPath = wavFilePath;

                        try
                        {
                            var wavReader = new WaveReader();
                            AudioData audioData;

                            using (var wavStream = File.OpenRead(wavFilePath))
                            {
                                audioData = wavReader.Read(wavStream);
                            }

                            var pcmFormat = audioData.GetFormat<Pcm16Format>();
                            int sampleRate = pcmFormat.SampleRate;

                            if (sampleRate != 48000 && sampleRate != 24000 && sampleRate != 16000 && sampleRate != 12000 && sampleRate != 8000)
                            {
                                ConversionProgress?.Invoke(this, $"重采样:{sampleRate}Hz->48000Hz");

                                int targetRate = 48000;
                                double ratio = (double)targetRate / sampleRate;

                                short[][] originalSamples = pcmFormat.Channels;
                                short[][] resampledSamples = ResampleSamples(originalSamples, ratio);

                                var newPcmFormat = new Pcm16Format(resampledSamples, targetRate);
                                var newAudioData = new AudioData(newPcmFormat);

                                processedWavPath = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.wav");

                                using (var tempStream = File.Create(processedWavPath))
                                {
                                    var wavWriter = new WaveWriter();
                                    wavWriter.WriteToStream(newAudioData, tempStream);
                                }

                                tempFiles.Add(processedWavPath);
                            }

                            StringBuilder errorMsg = new StringBuilder(256);
                            int result = _encodeFunc!(processedWavPath, lopusFilePath, errorMsg, errorMsg.Capacity);

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

                    foreach (var tempFile in tempFiles)
                    {
                        try
                        {
                            if (File.Exists(tempFile))
                                File.Delete(tempFile);
                        }
                        catch { }
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
