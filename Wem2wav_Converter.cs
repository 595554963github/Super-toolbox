using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wem2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public float? Volume { get; set; }
        public bool UseLegacyMode { get; set; }

        private static readonly byte[] JUNK =
        {
            0x06, 0x00, 0x00, 0x00, 0x02, 0x31, 0x00, 0x00, 0x4A, 0x55, 0x4E, 0x4B, 0x04, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };

        private struct WavHeader
        {
            public uint lengthofformatdata;
            public ushort type;
            public short channels;
            public uint samplerate;
            public int averagebytespersecond;
            public short blockalign;
            public short bitspersample;
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
                    var wemFiles = Directory.GetFiles(directoryPath, "*.wem", SearchOption.AllDirectories)
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

                    TotalFilesToConvert = wemFiles.Length;
                    int successCount = 0;

                    if (wemFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的WEM文件");
                        OnConversionFailed("未找到需要转换的WEM文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个WEM文件");

                    foreach (var wemFilePath in wemFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(wemFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.wem");

                        string fileDirectory = Path.GetDirectoryName(wemFilePath) ?? string.Empty;
                        string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                        try
                        {
                            if (File.Exists(wavFilePath))
                                File.Delete(wavFilePath);

                            bool conversionSuccess = UseLegacyMode
                                ? ConvertWemToWavLegacy(wemFilePath, wavFilePath)
                                : ConvertWemToWavAdvanced(wemFilePath, wavFilePath);

                            if (conversionSuccess && File.Exists(wavFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFilePath)}");
                                OnFileConverted(wavFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.wem转换失败");
                                OnConversionFailed($"{fileName}.wem转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.wem处理错误:{ex.Message}");
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
                }, cancellationToken);
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

        private bool ConvertWemToWavLegacy(string wemFilePath, string wavFilePath)
        {
            try
            {
                using (BinaryReader br = new BinaryReader(File.OpenRead(wemFilePath)))
                {
                    WavHeader header = ReadWemHeader(br);
                    byte[] data = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position));

                    using (BinaryWriter bw = new BinaryWriter(File.Create(wavFilePath)))
                    {
                        WriteWavHeader(bw, header);
                        bw.Write(data);

                        long size = bw.BaseStream.Length;
                        bw.BaseStream.Position = 0;

                        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                        bw.Write((uint)size);
                        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ConvertWemToWavAdvanced(string wemPath, string wavPath)
        {
            try
            {
                using var fs = new FileStream(wemPath, FileMode.Open);
                using var reader = new BinaryReader(fs);

                if (new string(reader.ReadChars(4)) != "RIFF")
                    return false;

                reader.ReadInt32();
                if (new string(reader.ReadChars(4)) != "WAVE")
                    return false;

                short numChannels = 0;
                int sampleRateFromFile = 0;
                short bitsPerSample = 0;
                short[] pcmData = null!;

                while (fs.Position < fs.Length)
                {
                    string chunkId = new string(reader.ReadChars(4));
                    int chunkSize = reader.ReadInt32();

                    if (chunkId == "fmt ")
                    {
                        reader.ReadInt16();
                        numChannels = reader.ReadInt16();
                        sampleRateFromFile = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadInt16();
                        bitsPerSample = reader.ReadInt16();

                        if (chunkSize > 16)
                            reader.ReadBytes(chunkSize - 16);
                    }
                    else if (chunkId == "data")
                    {
                        int dataSize = chunkSize;
                        int totalSamples = dataSize / (bitsPerSample / 8);
                        pcmData = new short[totalSamples];

                        if (bitsPerSample == 16)
                        {
                            for (int i = 0; i < totalSamples; i++)
                            {
                                pcmData[i] = reader.ReadInt16();
                            }
                        }
                        else if (bitsPerSample == 8)
                        {
                            for (int i = 0; i < totalSamples; i++)
                            {
                                pcmData[i] = (short)((reader.ReadByte() - 128) * 256);
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        reader.ReadBytes(chunkSize);
                    }
                }

                if (pcmData == null)
                    return false;

                int targetChannelsFinal = Channels ?? numChannels;
                int targetSampleRateFinal = SampleRate ?? sampleRateFromFile;

                short[] processedData = pcmData;

                if (targetChannelsFinal != numChannels)
                {
                    processedData = ConvertChannels(processedData, numChannels, targetChannelsFinal);
                }

                if (targetSampleRateFinal != sampleRateFromFile)
                {
                    processedData = ResampleSamples(processedData, sampleRateFromFile, targetSampleRateFinal);
                }

                string? parentDir = Path.GetDirectoryName(wavPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    Directory.CreateDirectory(parentDir);

                using var wavFs = new FileStream(wavPath, FileMode.Create);
                using var writer = new BinaryWriter(wavFs);

                int byteRate = targetSampleRateFinal * targetChannelsFinal * 2;
                short blockAlign = (short)(targetChannelsFinal * 2);
                int wavDataSize = processedData.Length * 2;

                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + wavDataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)targetChannelsFinal);
                writer.Write(targetSampleRateFinal);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(wavDataSize);

                foreach (short sample in processedData)
                {
                    writer.Write(sample);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private short[] ResampleSamples(short[] samples, int originalRate, int targetRate)
        {
            if (originalRate == targetRate)
                return samples;

            double ratio = (double)targetRate / originalRate;
            int newLength = (int)(samples.Length * ratio);
            short[] result = new short[newLength];

            for (int i = 0; i < newLength; i++)
            {
                double pos = i / ratio;
                int index = (int)pos;
                double frac = pos - index;

                if (index >= samples.Length - 1)
                {
                    result[i] = samples[samples.Length - 1];
                }
                else
                {
                    double sample = samples[index] * (1 - frac) + samples[index + 1] * frac;
                    result[i] = (short)sample;
                }
            }

            return result;
        }

        private short[] ConvertChannels(short[] samples, int sourceChannels, int targetChannels)
        {
            int frameCount = samples.Length / sourceChannels;
            short[] result = new short[frameCount * targetChannels];

            for (int i = 0; i < frameCount; i++)
            {
                for (int j = 0; j < targetChannels; j++)
                {
                    int sourceIndex = i * sourceChannels + (j % sourceChannels);
                    float sample = samples[sourceIndex] * (Volume ?? 1.0f);
                    if (sample > 32767) sample = 32767;
                    if (sample < -32768) sample = -32768;
                    result[i * targetChannels + j] = (short)sample;
                }
            }

            return result;
        }

        private WavHeader ReadWemHeader(BinaryReader br)
        {
            WavHeader header = new WavHeader();

            br.ReadBytes(4);
            br.ReadInt32();
            br.ReadBytes(4);
            br.ReadBytes(4);

            header.lengthofformatdata = 0x10;
            br.ReadInt32();
            header.type = br.ReadUInt16();
            header.channels = br.ReadInt16();
            header.samplerate = br.ReadUInt32();
            header.averagebytespersecond = br.ReadInt32();
            header.blockalign = br.ReadInt16();
            header.bitspersample = br.ReadInt16();

            br.ReadBytes(JUNK.Length);

            return header;
        }

        private void WriteWavHeader(BinaryWriter bw, WavHeader header)
        {
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(0u);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            bw.Write(Encoding.ASCII.GetBytes("fmt"));
            bw.Write((byte)0x20);
            bw.Write(16);
            bw.Write((byte)0x01);
            bw.Write((byte)0x00);
            bw.Write(header.channels);
            bw.Write(header.samplerate);
            bw.Write(header.averagebytespersecond);
            bw.Write(header.blockalign);
            bw.Write(header.bitspersample);
        }
    }
}