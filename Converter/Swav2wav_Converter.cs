using System.Diagnostics;
using System.Reflection;
using VGAudio.Containers.Wave;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Swav2wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static string? _tempExePath;
        private static bool _exeExtracted = false;
        private static readonly object _lock = new object();

        static Swav2wav_Converter()
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
                    _tempExePath = Path.Combine(tempDir, "swav2wav.exe");

                    if (!File.Exists(_tempExePath))
                    {
                        Assembly assembly = Assembly.GetExecutingAssembly();
                        string resourceName = "embedded.swav2wav.exe";

                        using (var stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream == null)
                                throw new FileNotFoundException($"嵌入的swav2wav资源未找到:{resourceName}");

                            byte[] buffer = new byte[stream.Length];
                            stream.Read(buffer, 0, buffer.Length);
                            File.WriteAllBytes(_tempExePath, buffer);
                        }
                    }

                    _exeExtracted = true;
                }
                catch (Exception ex)
                {
                    throw new Exception($"提取swav2wav.exe失败:{ex.Message}");
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
                    var swavFiles = Directory.GetFiles(directoryPath, "*.swav", SearchOption.AllDirectories)
                        .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                        .ToArray();

                    TotalFilesToConvert = swavFiles.Length;
                    int successCount = 0;

                    if (swavFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的SWAV文件");
                        OnConversionFailed("未找到需要转换的SWAV文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个SWAV文件");

                    foreach (var swavFilePath in swavFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(swavFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.swav");

                        string fileDirectory = Path.GetDirectoryName(swavFilePath) ?? string.Empty;
                        string monoWavPath = Path.Combine(fileDirectory, $"{fileName}.wav");
                        string tempWavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

                        try
                        {
                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"\"{swavFilePath}\"",
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WorkingDirectory = fileDirectory
                                }
                            };

                            process.Start();
                            process.WaitForExit();

                            if (process.ExitCode == 0 && File.Exists(monoWavPath))
                            {
                                var wavReader = new WaveReader();
                                using (var fs = File.OpenRead(monoWavPath))
                                {
                                    var audioData = wavReader.Read(fs);
                                    var pcmFormat = audioData.GetFormat<Pcm16Format>();

                                    short[][] stereoChannels = new short[2][];
                                    short[] monoData = pcmFormat.Channels[0];

                                    stereoChannels[0] = monoData;
                                    stereoChannels[1] = new short[monoData.Length];
                                    Array.Copy(monoData, stereoChannels[1], monoData.Length);

                                    var stereoFormat = new Pcm16Format(stereoChannels, pcmFormat.SampleRate);

                                    var wavWriter = new WaveWriter();
                                    using (var outFs = File.Create(tempWavPath))
                                    {
                                        wavWriter.WriteToStream(stereoFormat, outFs);
                                    }
                                }

                                File.Delete(monoWavPath);
                                File.Move(tempWavPath, monoWavPath);

                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav(立体声)");
                                OnFileConverted(monoWavPath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.swav转换失败");
                                OnConversionFailed($"{fileName}.swav转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.swav处理错误:{ex.Message}");
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