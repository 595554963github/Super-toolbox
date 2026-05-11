using System.Text.RegularExpressions;
using LightCodec;

namespace super_toolbox
{
    public class At3plus2wav_Converter : BaseExtractor
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
                            ConvertAt3plusToWav(at3FilePath, wavFile, cancellationToken));

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

        private bool ConvertAt3plusToWav(string at3FilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] data = File.ReadAllBytes(at3FilePath);
                int channels = 2;
                int sampleRate = 48000;
                int blockAlign = 638;
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
                            if (blockAlign <= 0) blockAlign = 638;
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

                ILightCodec codec = CodecFactory.Get(AudioCodec.AT3plus);
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

                // Write WAV file directly (no VGAudio dependency)
                WriteWavFile(wavFilePath, pcmData, outputOffset, sampleRate, channels);

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

        private static void WriteWavFile(string filePath, short[] pcmData, int totalSamples, int sampleRate, int channels)
        {
            int dataSize = totalSamples * sizeof(short);
            int fileSize = 36 + dataSize;

            using (var fs = File.Create(filePath))
            using (var bw = new BinaryWriter(fs))
            {
                // RIFF header
                bw.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                bw.Write(fileSize);
                bw.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

                // fmt chunk
                bw.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                bw.Write(16);                          // chunk size
                bw.Write((short)1);                    // PCM format
                bw.Write((short)channels);             // channels
                bw.Write(sampleRate);                  // sample rate
                bw.Write(sampleRate * channels * 2);   // byte rate
                bw.Write((short)(channels * 2));       // block align
                bw.Write((short)16);                   // bits per sample

                // data chunk
                bw.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                bw.Write(dataSize);

                // Write PCM data
                for (int i = 0; i < totalSamples; i++)
                {
                    bw.Write(pcmData[i]);
                }
            }
        }
    }
}