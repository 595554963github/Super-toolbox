using VGAudio.Formats.Pcm16;
using VGAudio.Containers.Wave;
using System.Text;

namespace super_toolbox
{
    public class Vag2wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        public int OutputSampleRate { get; set; } = 44100;
        public bool ForceStereo { get; set; } = true;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var vagFiles = Directory.GetFiles(directoryPath, "*.vag", SearchOption.AllDirectories)
                .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = vagFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var vagFilePath in vagFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(vagFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.vag");

                    string fileDirectory = Path.GetDirectoryName(vagFilePath) ?? string.Empty;

                    try
                    {
                        bool conversionSuccess = await ConvertVagToWav(
                            vagFilePath,
                            fileDirectory,
                            fileName,
                            cancellationToken);

                        if (conversionSuccess)
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{fileName}");
                            OnFileConverted(Path.Combine(fileDirectory, $"{fileName}.wav"));
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.vag转换失败");
                            OnConversionFailed($"{fileName}.vag转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.vag处理错误:{ex.Message}");
                    }
                }

                ConversionProgress?.Invoke(this,
                    $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
        }

        private short ClipInt16(int value)
        {
            if ((value + 0x8000) > 0xFFFF)
                return (short)((value >> 31) ^ 0x7FFF);
            return (short)value;
        }

        private async Task<bool> ConvertVagToWav(
            string vagFilePath,
            string outputDir,
            string fileName,
            CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取VAG文件:{Path.GetFileName(vagFilePath)}");

                using var fs = File.OpenRead(vagFilePath);
                using var reader = new BinaryReader(fs);

                byte[] magicBytes = reader.ReadBytes(4);
                string magic = Encoding.ASCII.GetString(magicBytes);
                if (magic != "VAGp")
                {
                    throw new InvalidOperationException("不是有效的VAG文件");
                }

                uint version = ReadUint32BE(reader);
                uint reserved = ReadUint32BE(reader);
                uint dataSize = ReadUint32BE(reader);
                uint sampleRate = ReadUint32BE(reader);

                reader.ReadBytes(12);

                byte[] nameBytes = reader.ReadBytes(16);
                string vagName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                reader.ReadBytes(16);

                double[,] lpc = new double[,]
                {
                    { 0.0, 0.0 },
                    { 60.0 / 64.0, 0.0 },
                    { 115.0 / 64.0, -52.0 / 64.0 },
                    { 98.0 / 64.0, -55.0 / 64.0 },
                    { 122.0 / 64.0, -60.0 / 64.0 }
                };

                List<short> pcmSamples = new List<short>();
                double s_1 = 0.0;
                double s_2 = 0.0;
                double[] frameSamples = new double[28];

                long dataEnd = dataSize + 48;

                while (fs.Position < dataEnd)
                {
                    int predict_nr_shift = reader.ReadByte();
                    int shift_factor = predict_nr_shift & 0xF;
                    int predict_nr = predict_nr_shift >> 4;

                    int flags = reader.ReadByte();

                    if (flags == 7)
                        break;

                    for (int i = 0; i < 28; i += 2)
                    {
                        int d = reader.ReadByte();

                        int s = (d & 0xF) << 12;
                        if ((s & 0x8000) != 0)
                            s |= unchecked((int)0xFFFF0000);
                        frameSamples[i] = (double)(s >> shift_factor);

                        s = (d & 0xF0) << 8;
                        if ((s & 0x8000) != 0)
                            s |= unchecked((int)0xFFFF0000);
                        frameSamples[i + 1] = (double)(s >> shift_factor);
                    }

                    for (int i = 0; i < 28; i++)
                    {
                        frameSamples[i] = frameSamples[i] + s_1 * lpc[predict_nr, 0] + s_2 * lpc[predict_nr, 1];
                        s_2 = s_1;
                        s_1 = frameSamples[i];

                        int sampleInt = (int)(frameSamples[i] + 0.5);
                        short sample = ClipInt16(sampleInt);

                        pcmSamples.Add(sample);
                    }
                }

                short[] samples = pcmSamples.ToArray();
                short[][] channels;

                if (ForceStereo)
                {
                    channels = new short[2][];
                    channels[0] = samples;
                    channels[1] = (short[])samples.Clone();
                }
                else
                {
                    channels = new short[1][];
                    channels[0] = samples;
                }

                string wavFile = Path.Combine(outputDir, $"{fileName}.wav");
                await SaveAsWav(channels, (int)sampleRate, wavFile);

                return true;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }

        private async Task SaveAsWav(short[][] channels, int sampleRate, string wavFile)
        {
            int targetSampleRate = sampleRate;

            var pcmFormat = new Pcm16Format(channels, targetSampleRate);
            var waveWriter = new WaveWriter();

            using var ms = new MemoryStream();
            waveWriter.WriteToStream(pcmFormat, ms);
            ms.Position = 0;

            using var fs = File.Create(wavFile);
            await ms.CopyToAsync(fs);
        }

        private uint ReadUint32BE(BinaryReader reader)
        {
            uint b1 = reader.ReadByte();
            uint b2 = reader.ReadByte();
            uint b3 = reader.ReadByte();
            uint b4 = reader.ReadByte();
            return (b1 << 24) | (b2 << 16) | (b3 << 8) | b4;
        }
    }
}