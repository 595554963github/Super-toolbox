using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2wem_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

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
                    string wemFilePath = Path.Combine(fileDirectory, $"{fileName}.wem");

                    try
                    {
                        if (File.Exists(wemFilePath))
                            File.Delete(wemFilePath);

                        bool conversionSuccess = await Task.Run(() => ConvertWavToWem(wavFilePath, wemFilePath), cancellationToken);

                        if (conversionSuccess && File.Exists(wemFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wemFilePath)}");
                            OnFileConverted(wemFilePath);
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

        private bool ConvertWavToWem(string wavFilePath, string wemFilePath)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取WAV文件:{Path.GetFileName(wavFilePath)}");

                using (BinaryReader br = new BinaryReader(File.OpenRead(wavFilePath)))
                {
                    WavHeader header = ReadWavHeader(br);

                    if (header.type != 1)
                    {
                        ConversionError?.Invoke(this, $"PCM格式,当前类型:{header.type}");
                        return false;
                    }

                    ConversionProgress?.Invoke(this, $"声道数:{header.channels},采样率:{header.samplerate},比特率:{header.bitspersample}");

                    byte[] data = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position));

                    using (BinaryWriter bw = new BinaryWriter(File.Create(wemFilePath)))
                    {
                        WriteWemHeader(bw, header);
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
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }

        private WavHeader ReadWavHeader(BinaryReader br)
        {
            WavHeader header = new WavHeader();

            br.ReadBytes(4);
            br.ReadInt32();
            br.ReadBytes(4);
            br.ReadBytes(4);

            header.lengthofformatdata = br.ReadUInt32();
            header.type = br.ReadUInt16();
            header.channels = br.ReadInt16();
            header.samplerate = br.ReadUInt32();
            header.averagebytespersecond = br.ReadInt32();
            header.blockalign = br.ReadInt16();
            header.bitspersample = br.ReadInt16();

            return header;
        }

        private void WriteWemHeader(BinaryWriter bw, WavHeader header)
        {
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(0u);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            bw.Write(Encoding.ASCII.GetBytes("fmt"));
            bw.Write((byte)0x20);
            bw.Write(header.lengthofformatdata + 8);
            bw.Write((byte)0xFE);
            bw.Write((byte)0xFF);
            bw.Write(header.channels);
            bw.Write(header.samplerate);
            bw.Write(header.averagebytespersecond);
            bw.Write(header.blockalign);
            bw.Write(header.bitspersample);
            bw.Write(JUNK);
        }
    }
}