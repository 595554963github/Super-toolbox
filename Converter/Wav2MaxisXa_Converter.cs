using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2MaxisXa_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private async Task<bool> ConvertWAVToMaxisXa(string wavFilePath, string xaFilePath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取WAV文件:{Path.GetFileName(wavFilePath)}");

                var wavReader = new WaveReader();
                AudioData audioData;
                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = wavReader.Read(wavStream);
                }

                if (audioData == null)
                {
                    ConversionError?.Invoke(this, "无法读取WAV音频数据");
                    return false;
                }

                Pcm16Format pcm16 = audioData.GetFormat<Pcm16Format>();
                if (pcm16 == null)
                {
                    var allFormats = audioData.GetAllFormats().ToList();
                    if (allFormats.Count > 0)
                    {
                        pcm16 = allFormats.First().ToPcm16();
                    }
                    else
                    {
                        ConversionError?.Invoke(this, "无法转换WAV格式");
                        return false;
                    }
                }

                int nChannels = pcm16.ChannelCount;
                int nSamples = pcm16.SampleCount;
                short[][] channels = pcm16.Channels;

                return await Task.Run(() =>
                {
                    using var outFile = File.Create(xaFilePath);

                    byte[] header = new byte[24];
                    int pos = 0;
                    header[pos++] = (byte)'X';
                    header[pos++] = (byte)'A';
                    header[pos++] = 0x00;
                    header[pos++] = 0x00;
                    WriteLE32(header, ref pos, (uint)(nSamples * 2 * nChannels));
                    WriteLE16(header, ref pos, 1);
                    WriteLE16(header, ref pos, (ushort)nChannels);
                    WriteLE32(header, ref pos, (uint)pcm16.SampleRate);
                    WriteLE32(header, ref pos, (uint)(nChannels * pcm16.SampleRate * 2));
                    WriteLE16(header, ref pos, (ushort)(nChannels * 2));
                    WriteLE16(header, ref pos, 16);
                    outFile.Write(header, 0, 24);

                    var encoders = new EaXaEncoder[nChannels];
                    for (int c = 0; c < nChannels; c++)
                        encoders[c] = new EaXaEncoder();

                    int codedSamples = 0;
                    bool lastBlock = false;
                    int blockSize = 15 * nChannels;
                    byte[] block = new byte[blockSize];
                    short[] samples = new short[28 * nChannels];

                    while (!lastBlock)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int samplesInBlock = 28;
                        codedSamples += samplesInBlock;
                        if (codedSamples >= nSamples)
                        {
                            int toRemove = codedSamples - nSamples;
                            samplesInBlock -= toRemove;
                            codedSamples = nSamples;
                            lastBlock = true;
                        }

                        ReadSamples(channels, samples, nSamples, nChannels, codedSamples - samplesInBlock, samplesInBlock);

                        if (samplesInBlock < 28)
                            Array.Clear(block, 0, blockSize);

                        int nBytes = 1 + (samplesInBlock + 1) / 2;
                        for (int c = 0; c < nChannels; c++)
                        {
                            byte[] encoded = new byte[15];
                            encoders[c].EncodeSubblock(samples, c * samplesInBlock, encoded, 0, samplesInBlock);
                            for (int b = 0; b < nBytes; b++)
                                block[c + b * nChannels] = encoded[b];
                        }

                        outFile.Write(block, 0, blockSize);
                    }

                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                return false;
            }
        }

        private static void ReadSamples(short[][] channels, short[] output, int totalSamples, int nChannels, int offset, int count)
        {
            for (int c = 0; c < nChannels; c++)
            {
                var ch = channels[c];
                int srcStart = Math.Min(offset, ch.Length);
                int srcCount = Math.Min(count, Math.Max(0, ch.Length - srcStart));
                int dstStart = c * count;
                if (srcCount > 0)
                    Array.Copy(ch, srcStart, output, dstStart, srcCount);
                for (int i = srcCount; i < count; i++)
                    output[dstStart + i] = 0;
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
                bool success = await ConvertWAVToMaxisXa(wavFilePath, outputPath, cancellationToken);

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
                    string xaFilePath = Path.Combine(fileDirectory, $"{fileName}.xa");

                    try
                    {
                        bool conversionSuccess = await ConvertWAVToMaxisXa(wavFilePath, xaFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(xaFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(xaFilePath)}");
                            OnFileConverted(xaFilePath);
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

        private static void WriteLE32(byte[] buf, ref int pos, uint val)
        {
            buf[pos++] = (byte)(val & 0xFF);
            buf[pos++] = (byte)((val >> 8) & 0xFF);
            buf[pos++] = (byte)((val >> 16) & 0xFF);
            buf[pos++] = (byte)((val >> 24) & 0xFF);
        }

        private static void WriteLE16(byte[] buf, ref int pos, ushort val)
        {
            buf[pos++] = (byte)(val & 0xFF);
            buf[pos++] = (byte)((val >> 8) & 0xFF);
        }
    }
}