using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2snr_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private async Task<bool> ConvertWAVToXAS(string wavFilePath, string xasFilePath, int loop, CancellationToken cancellationToken)
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
                int sampleRate = pcm16.SampleRate;
                bool isLoop = loop != 0;

                return await Task.Run(() =>
                {
                    using var outFile = File.Create(xasFilePath);

                    byte[] header = new byte[isLoop ? 24 : 20];
                    int pos = 0;
                    header[pos++] = 0x04;
                    header[pos++] = (byte)((nChannels - 1) * 4);
                    header[pos++] = (byte)((sampleRate >> 8) & 0xFF);
                    header[pos++] = (byte)(sampleRate & 0xFF);
                    uint samplesFlags = (uint)nSamples | (isLoop ? (1u << 29) : 0);
                    header[pos++] = (byte)((samplesFlags >> 24) & 0xFF);
                    header[pos++] = (byte)((samplesFlags >> 16) & 0xFF);
                    header[pos++] = (byte)((samplesFlags >> 8) & 0xFF);
                    header[pos++] = (byte)(samplesFlags & 0xFF);
                    if (isLoop)
                    {
                        header[pos++] = 0; header[pos++] = 0;
                        header[pos++] = 0; header[pos++] = 0;
                    }
                    int offsetBlockSizeValue = pos;
                    pos += 4;
                    header[pos++] = (byte)(((uint)nSamples >> 24) & 0xFF);
                    header[pos++] = (byte)(((uint)nSamples >> 16) & 0xFF);
                    header[pos++] = (byte)(((uint)nSamples >> 8) & 0xFF);
                    header[pos++] = (byte)((uint)nSamples & 0xFF);
                    outFile.Write(header, 0, pos);

                    var encoders = new EaXaEncoder[nChannels];
                    for (int c = 0; c < nChannels; c++)
                        encoders[c] = new EaXaEncoder();

                    int codedSamples = 0;
                    bool lastBlock = false;
                    byte[] block = new byte[76 * nChannels];
                    short[] samples = new short[128 * nChannels];

                    while (!lastBlock)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int samplesInBlock = 128;
                        codedSamples += samplesInBlock;
                        if (codedSamples >= nSamples)
                        {
                            int toRemove = codedSamples - nSamples;
                            samplesInBlock -= toRemove;
                            codedSamples = nSamples;
                            lastBlock = true;
                        }

                        ReadSamples(channels, samples, nSamples, nChannels, codedSamples - samplesInBlock, samplesInBlock);

                        int blockPos = 0;
                        EncodeXasBlock(encoders, samples, block, ref blockPos, samplesInBlock, nChannels);
                        outFile.Write(block, 0, blockPos);
                    }

                    long endPos = outFile.Length;
                    outFile.Seek(offsetBlockSizeValue, SeekOrigin.Begin);
                    uint blockSizeVal = (uint)(endPos - offsetBlockSizeValue);
                    byte[] bsBuf = new byte[4];
                    bsBuf[0] = (byte)((blockSizeVal >> 24) & 0xFF);
                    bsBuf[1] = (byte)((blockSizeVal >> 16) & 0xFF);
                    bsBuf[2] = (byte)((blockSizeVal >> 8) & 0xFF);
                    bsBuf[3] = (byte)(blockSizeVal & 0xFF);
                    outFile.Write(bsBuf, 0, 4);

                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                return false;
            }
        }

        private static void EncodeXasBlock(EaXaEncoder[] encoders, short[] samples, byte[] output, ref int outPos, int nSamples, int nChannels)
        {
            for (int c = 0; c < nChannels; c++)
            {
                short[] inputSamples = new short[128];
                int srcOffset = c * nSamples;
                int copyCount = Math.Min(nSamples, 128);
                Array.Copy(samples, srcOffset, inputSamples, 0, copyCount);

                short[][] startSamples = new short[4][];
                for (int i = 0; i < 4; i++) startSamples[i] = new short[2];
                byte[][] encoded = new byte[4][];
                for (int i = 0; i < 4; i++) encoded[i] = new byte[16];

                EaXaEncoder encoder = encoders[c];

                for (int i = 0; i < 4; i++)
                {
                    int grpOffset = i * 32;
                    startSamples[i][0] = (short)(EaXaEncoder.ClipInt16(inputSamples[grpOffset] + 8) & 0xFFF0);
                    startSamples[i][1] = (short)(EaXaEncoder.ClipInt16(inputSamples[grpOffset + 1] + 8) & 0xFFF0);
                    encoder.PreviousSample = startSamples[i][0];
                    encoder.CurrentSample = startSamples[i][1];
                    encoder.ClearErrors();
                    encoder.EncodeSubblock(inputSamples, grpOffset + 2, encoded[i], 0, 30);
                }

                for (int i = 0; i < 4; i++)
                {
                    byte infoByte = encoded[i][0];
                    ushort val0 = (ushort)((ushort)startSamples[i][0] | (ushort)(infoByte >> 4));
                    ushort val1 = (ushort)((ushort)startSamples[i][1] | (ushort)(infoByte & 0x0F));
                    output[outPos++] = (byte)(val0 & 0xFF);
                    output[outPos++] = (byte)((val0 >> 8) & 0xFF);
                    output[outPos++] = (byte)(val1 & 0xFF);
                    output[outPos++] = (byte)((val1 >> 8) & 0xFF);
                }

                for (int j = 1; j <= 15; j++)
                {
                    for (int i = 0; i < 4; i++)
                        output[outPos++] = encoded[i][j];
                }
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

        public async Task ExtractSingleAsync(string wavFilePath, string outputPath, int loop = 0, CancellationToken cancellationToken = default)
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
                bool success = await ConvertWAVToXAS(wavFilePath, outputPath, loop, cancellationToken);

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
                    string xasFilePath = Path.Combine(fileDirectory, $"{fileName}.snr");

                    try
                    {
                        bool conversionSuccess = await ConvertWAVToXAS(wavFilePath, xasFilePath, 0, cancellationToken);

                        if (conversionSuccess && File.Exists(xasFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(xasFilePath)}");
                            OnFileConverted(xasFilePath);
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