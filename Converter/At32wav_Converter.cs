using System.Text.RegularExpressions;
using LightCodec;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class At32wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var at3Files = Directory.GetFiles(directoryPath, "*.at3", SearchOption.AllDirectories)
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

            TotalFilesToConvert = at3Files.Length;
            int successCount = 0;

            try
            {
                foreach (var at3FilePath in at3Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(at3FilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.at3");

                    string fileDirectory = Path.GetDirectoryName(at3FilePath) ?? string.Empty;

                    try
                    {
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertAt3ToWav(at3FilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.at3转换失败");
                            OnConversionFailed($"{fileName}.at3转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.at3处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
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

        private bool ConvertAt3ToWav(string at3FilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] data = File.ReadAllBytes(at3FilePath);
                int channels = 2;
                int sampleRate = 44100;
                int blockAlign = 96;
                byte[]? audioData = null;

                if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')
                {
                    int offset = 12;
                    while (offset + 8 <= data.Length)
                    {
                        string chunkId = System.Text.Encoding.ASCII.GetString(data, offset, 4);
                        int chunkSize = BitConverter.ToInt32(data, offset + 4);
                        offset += 8;

                        if (chunkId == "fmt ")
                        {
                            channels = BitConverter.ToInt16(data, offset + 2);
                            sampleRate = BitConverter.ToInt32(data, offset + 4);
                            blockAlign = BitConverter.ToInt16(data, offset + 12);
                            if (blockAlign <= 0) blockAlign = 96;
                        }
                        else if (chunkId == "data")
                        {
                            audioData = new byte[chunkSize];
                            Array.Copy(data, offset, audioData, 0, chunkSize);
                            break;
                        }
                        offset += chunkSize;
                    }
                }
                else
                {
                    audioData = data;
                }

                if (audioData == null || audioData.Length == 0)
                    return false;

                ILightCodec codec = CodecFactory.Get(AudioCodec.AT3);
                int initResult = codec.init(blockAlign, channels, channels, 0);
                if (initResult < 0)
                    return false;

                int samplesPerFrame = codec.NumberOfSamples;
                int totalFrames = audioData.Length / blockAlign;
                int totalSamples = totalFrames * samplesPerFrame;
                short[] pcmData = new short[totalSamples * channels];

                int outputOffset = 0;
                int inputOffset = 0;

                unsafe
                {
                    fixed (byte* inputPtr = audioData)
                    {
                        while (inputOffset + blockAlign <= audioData.Length && outputOffset < pcmData.Length)
                        {
                            fixed (short* outputPtr = &pcmData[outputOffset])
                            {
                                int bytesWritten;
                                int consumed = codec.decode(inputPtr + inputOffset, blockAlign, outputPtr, out bytesWritten);
                                if (consumed <= 0)
                                    break;
                                outputOffset += bytesWritten / sizeof(short);
                            }
                            inputOffset += blockAlign;
                        }
                    }
                }

                if (outputOffset == 0)
                    return false;

                short[][] channelData = new short[channels][];
                int samplesPerChannel = outputOffset / channels;
                
                for (int c = 0; c < channels; c++)
                {
                    channelData[c] = new short[samplesPerChannel];
                    for (int i = 0; i < samplesPerChannel; i++)
                    {
                        channelData[c][i] = pcmData[i * channels + c];
                    }
                }

                var pcmFormat = new Pcm16Format(channelData, sampleRate);
                var audio = new AudioData(pcmFormat);

                var waveConfig = new WaveConfiguration
                {
                    Codec = WaveCodec.Pcm16Bit
                };

                var waveWriter = new WaveWriter();
                using (var stream = File.Create(wavFilePath))
                {
                    waveWriter.WriteToStream(audio, stream, waveConfig);
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
    }
}