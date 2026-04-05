using System.Text;
using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2aifc_Converter : BaseExtractor
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
                    string aifcFile = Path.Combine(fileDirectory, $"{fileName}.aifc");

                    try
                    {
                        if (File.Exists(aifcFile))
                        {
                            File.Delete(aifcFile);
                        }

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToAifc(wavFilePath, aifcFile, cancellationToken));

                        if (conversionSuccess && File.Exists(aifcFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(aifcFile)}");
                            OnFileConverted(aifcFile);
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

        private bool ConvertWavToAifc(string wavFilePath, string aifcFilePath, CancellationToken cancellationToken)
        {
            try
            {
                var waveReader = new WaveReader();
                AudioData audioData;

                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = waveReader.Read(wavStream);
                }

                if (audioData == null)
                {
                    throw new InvalidOperationException("无法读取WAV音频数据");
                }

                var pcmFormat = audioData.GetFormat<Pcm16Format>();
                var shortChannels = pcmFormat.Channels;
                int channels = shortChannels.Length;
                int samples = pcmFormat.SampleCount;
                int sampleRate = pcmFormat.SampleRate;

                int bytesPerSample = 2;
                int dataSize = samples * channels * bytesPerSample;
                byte[] pcmData = new byte[dataSize];

                for (int s = 0; s < samples; s++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        short value = shortChannels[c][s];
                        int offset = (s * channels + c) * bytesPerSample;
                        pcmData[offset] = (byte)((value >> 8) & 0xFF);
                        pcmData[offset + 1] = (byte)(value & 0xFF);
                    }
                }

                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    int commChunkSize = 22;
                    int ssndChunkDataSize = 8 + dataSize;
                    if (ssndChunkDataSize % 2 != 0) ssndChunkDataSize++;

                    int formSize = 4 + commChunkSize + 8 + ssndChunkDataSize;
                    if (formSize % 2 != 0) formSize++;

                    writer.Write(Encoding.ASCII.GetBytes("FORM"));
                    writer.Write(ToBigEndian(formSize));
                    writer.Write(Encoding.ASCII.GetBytes("AIFC"));

                    writer.Write(Encoding.ASCII.GetBytes("COMM"));
                    writer.Write(ToBigEndian(commChunkSize));
                    writer.Write(ToBigEndian((short)channels));
                    writer.Write(ToBigEndian((uint)samples));
                    writer.Write(ToBigEndian((short)16));
                    WriteExtended80(writer, sampleRate);
                    writer.Write(Encoding.ASCII.GetBytes("NONE"));

                    writer.Write(Encoding.ASCII.GetBytes("SSND"));
                    writer.Write(ToBigEndian(ssndChunkDataSize));
                    writer.Write(ToBigEndian(0));
                    writer.Write(ToBigEndian(0));
                    writer.Write(pcmData);

                    if (dataSize % 2 != 0)
                    {
                        writer.Write((byte)0);
                    }

                    File.WriteAllBytes(aifcFilePath, ms.ToArray());
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

        private byte[] ToBigEndian(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private byte[] ToBigEndian(uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private byte[] ToBigEndian(short value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private byte[] ToBigEndian(ushort value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private byte[] ToBigEndian(ulong value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private void WriteExtended80(BinaryWriter writer, int sampleRate)
        {
            if (sampleRate <= 0)
            {
                for (int i = 0; i < 10; i++)
                    writer.Write((byte)0);
                return;
            }

            double value = (double)sampleRate;
            long bits = BitConverter.DoubleToInt64Bits(value);

            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFF;

            if (exponent == 0)
            {
                writer.Write(ToBigEndian((ushort)0));
                writer.Write(ToBigEndian((ulong)0));
                return;
            }

            int aiffExponent = exponent - 1023 + 16383;
            ulong aiffMantissa = ((ulong)1 << 63) | ((ulong)mantissa << 11);

            writer.Write(ToBigEndian((ushort)aiffExponent));
            writer.Write(ToBigEndian(aiffMantissa));
        }
    }
}