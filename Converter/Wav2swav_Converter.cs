using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2swav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static string? _tempExePath;
        private static bool _exeExtracted = false;
        private static readonly object _lock = new object();

        static Wav2swav_Converter()
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
                    _tempExePath = Path.Combine(tempDir, "wav2swav.exe");

                    if (!File.Exists(_tempExePath))
                    {
                        Assembly assembly = Assembly.GetExecutingAssembly();
                        string resourceName = "embedded.wav2swav.exe";

                        using (var stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream == null)
                                throw new FileNotFoundException($"嵌入的wav2swav资源未找到:{resourceName}");

                            byte[] buffer = new byte[stream.Length];
                            stream.Read(buffer, 0, buffer.Length);
                            File.WriteAllBytes(_tempExePath, buffer);
                        }
                    }

                    _exeExtracted = true;
                }
                catch (Exception ex)
                {
                    throw new Exception($"提取wav2swav.exe失败:{ex.Message}");
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
                        string swavFilePath = Path.Combine(fileDirectory, $"{fileName}.swav");

                        try
                        {
                            if (ConvertWavToSwav(wavFilePath, swavFilePath) && File.Exists(swavFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.swav");
                                OnFileConverted(swavFilePath);
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

        private bool ConvertWavToSwav(string wavFilePath, string swavFilePath)
        {
            try
            {
                var wavReader = new WaveReader();
                AudioData audioData;

                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = wavReader.Read(wavStream);
                }

                if (audioData == null)
                    return false;

                var pcmFormat = audioData.GetFormat<Pcm16Format>();
                int sampleRate = pcmFormat.SampleRate;

                short[] monoPcm = MixToMono(pcmFormat);

                string tempWav = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
                string tempSwav = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".swav");

                try
                {
                    using (var tempStream = File.Create(tempWav))
                    {
                        var wavWriter = new WaveWriter();
                        var monoFormat = new Pcm16Format(new short[][] { monoPcm }, sampleRate);
                        wavWriter.WriteToStream(monoFormat, tempStream);
                    }

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"\"{tempWav}\"",
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

                    string possibleSwav = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(tempWav) + ".swav");

                    if (process.ExitCode == 0 && File.Exists(possibleSwav))
                    {
                        File.Copy(possibleSwav, swavFilePath, true);
                        return true;
                    }

                    if (File.Exists(tempSwav))
                    {
                        File.Copy(tempSwav, swavFilePath, true);
                        return true;
                    }

                    return false;
                }
                finally
                {
                    try { File.Delete(tempWav); } catch { }
                    try { File.Delete(tempSwav); } catch { }
                    try { File.Delete(Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(tempWav) + ".swav")); } catch { }
                }
            }
            catch
            {
                return false;
            }
        }

        private short[] MixToMono(Pcm16Format pcmFormat)
        {
            short[][] channels = pcmFormat.Channels;
            int channelCount = channels.Length;
            int sampleCount = channels[0].Length;

            if (channelCount == 1)
                return channels[0];

            short[] mono = new short[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int sum = 0;
                for (int ch = 0; ch < channelCount; ch++)
                {
                    sum += channels[ch][i];
                }
                mono[i] = (short)(sum / channelCount);
            }

            return mono;
        }
    }
}