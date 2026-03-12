using System.Text;

namespace super_toolbox
{
    public class Wem2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

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

            var wemFiles = Directory.GetFiles(directoryPath, "*.wem", SearchOption.AllDirectories);
            TotalFilesToConvert = wemFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var wemFilePath in wemFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(wemFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.wem");

                    string fileDirectory = Path.GetDirectoryName(wemFilePath) ?? string.Empty;
                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavFilePath))
                            File.Delete(wavFilePath);

                        bool conversionSuccess = await Task.Run(() => ConvertWemToWav(wemFilePath, wavFilePath), cancellationToken);

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

        private bool ConvertWemToWav(string wemFilePath, string wavFilePath)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"读取WEM文件:{Path.GetFileName(wemFilePath)}");

                using (BinaryReader br = new BinaryReader(File.OpenRead(wemFilePath)))
                {
                    WavHeader header = ReadWemHeader(br);

                    ConversionProgress?.Invoke(this, $"声道数:{header.channels},采样率:{header.samplerate},比特率:{header.bitspersample}");

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
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
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