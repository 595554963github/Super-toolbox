using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Snr2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static uint Swap(uint le)
        {
            uint num = le & 0xFF;
            uint num2 = (le >> 8) & 0xFF;
            uint num3 = (le >> 16) & 0xFF;
            uint num4 = (le >> 24) & 0xFF;
            return (num << 24) | (num2 << 16) | (num3 << 8) | num4;
        }

        private static void WriteWavHeader(BinaryWriter bw, int channels, int samplerate, int bytes)
        {
            bw.Write('R');
            bw.Write('I');
            bw.Write('F');
            bw.Write('F');
            bw.Write(bytes + 36);
            bw.Write('W');
            bw.Write('A');
            bw.Write('V');
            bw.Write('E');
            bw.Write('f');
            bw.Write('m');
            bw.Write('t');
            bw.Write(' ');
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)channels);
            bw.Write(samplerate);
            bw.Write(samplerate * channels * 2);
            bw.Write((short)(channels * 2));
            bw.Write((short)16);
            bw.Write('d');
            bw.Write('a');
            bw.Write('t');
            bw.Write('a');
            bw.Write(bytes);
        }

        private static bool ConvertSnrToWav(string snrFilePath, string wavFilePath)
        {
            try
            {
                using var fs = new FileStream(snrFilePath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                byte[] buffer = new byte[76];
                short[] sampleBuffer = new short[1024];
                int[] coeffs = new int[32];
                int[] coeff4 = { 0, 240, 460, 392 };
                int[] coeff5 = { 0, 0, -208, -220 };

                int channels = 0;
                int samplerate = 0;
                int chunkCount = 0;

                while (fs.Position < fs.Length)
                {
                    int marker = br.ReadByte();
                    if (marker == 4)
                    {
                        channels = (br.ReadByte() >> 2) + 1;
                        if (channels > 8 || channels < 1)
                        {
                            return false;
                        }
                        samplerate = br.ReadByte() * 256 + br.ReadByte();
                    }
                    else if (marker == 72)
                    {
                        fs.Seek(-1, SeekOrigin.Current);
                        Swap(br.ReadUInt32());
                        marker = br.ReadByte();
                        if (marker != 20 && marker != 18)
                        {
                            return false;
                        }
                        channels = (br.ReadByte() >> 2) + 1;
                        if (channels > 8 || channels < 1)
                        {
                            return false;
                        }
                        samplerate = br.ReadByte() * 256 + br.ReadByte();
                    }
                    else if (marker == 1)
                    {
                        fs.Seek(-1, SeekOrigin.Current);
                        uint val = br.ReadUInt32();
                        if (val != 4097)
                        {
                            return false;
                        }
                        do
                        {
                            val = br.ReadUInt32();
                        }
                        while (val != 201326664);
                        fs.Seek(-4, SeekOrigin.Current);
                        continue;
                    }
                    else
                    {
                        return false;
                    }

                    uint totalSamples = br.ReadUInt32();
                    if ((totalSamples & 32) == 32)
                    {
                        br.ReadInt32();
                    }
                    if ((totalSamples & 96) == 96)
                    {
                        br.ReadInt32();
                    }

                    totalSamples = Swap(totalSamples) & 0x0FFFFFFF;
                    uint samplesRead = 0;

                    string outputPath = wavFilePath;
                    if (chunkCount > 0)
                    {
                        string dir = Path.GetDirectoryName(wavFilePath) ?? "";
                        string name = Path.GetFileNameWithoutExtension(wavFilePath);
                        outputPath = Path.Combine(dir, $"{name}_{chunkCount}.wav");
                    }

                    using var fsOut = new FileStream(outputPath, FileMode.Create);
                    using var bw = new BinaryWriter(fsOut);

                    WriteWavHeader(bw, channels, samplerate, (int)(totalSamples * (uint)channels * 2));

                    while (fs.Position < fs.Length)
                    {
                        uint blockSize = Swap(br.ReadUInt32());
                        if (marker == 20 || marker == 18)
                        {
                            if ((blockSize & 0xFF000000) == 0x45000000)
                                break;
                            blockSize &= 0x00FFFFFF;
                        }
                        else if ((blockSize & 0x80000000) > 0)
                        {
                            blockSize &= 0x00FFFFFF;
                        }

                        uint samplesInBlock = Swap(br.ReadUInt32());
                        if (blockSize == 0 || samplesInBlock == 0)
                            break;

                        blockSize -= 8;
                        samplesRead += samplesInBlock;

                        int numBlocks = (int)(blockSize / 76 / (uint)channels);

                        if (marker == 18)
                        {
                            for (int s = 0; s < samplesInBlock * channels; s++)
                            {
                                int b1 = fs.ReadByte();
                                int b2 = fs.ReadByte();
                                if (b1 == -1 || b2 == -1) break;
                                short sample = (short)((b1 << 8) | b2);
                                bw.Write(sample);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < numBlocks; i++)
                            {
                                for (int ch = 0; ch < channels; ch++)
                                {
                                    fs.Read(buffer, 0, 76);
                                    for (int k = 0; k < 4; k++)
                                    {
                                        coeffs[0] = (short)((buffer[k * 4] & 0xF0) | (buffer[k * 4 + 1] << 8));
                                        coeffs[1] = (short)((buffer[k * 4 + 2] & 0xF0) | (buffer[k * 4 + 3] << 8));
                                        int idx1 = buffer[k * 4] & 0x0F;
                                        int idx2 = buffer[k * 4 + 2] & 0x0F;

                                        for (int l = 2; l < 32; l += 2)
                                        {
                                            int nibble = (buffer[12 + k + l * 2] & 0xF0) >> 4;
                                            if (nibble > 7) nibble -= 16;
                                            int pred = coeffs[l - 1] * coeff4[idx1] + coeffs[l - 2] * coeff5[idx1];
                                            coeffs[l] = (pred + (nibble << (20 - idx2)) + 128) >> 8;
                                            coeffs[l] = Math.Max(-32768, Math.Min(32767, coeffs[l]));

                                            nibble = buffer[12 + k + l * 2] & 0x0F;
                                            if (nibble > 7) nibble -= 16;
                                            pred = coeffs[l] * coeff4[idx1] + coeffs[l - 1] * coeff5[idx1];
                                            coeffs[l + 1] = (pred + (nibble << (20 - idx2)) + 128) >> 8;
                                            coeffs[l + 1] = Math.Max(-32768, Math.Min(32767, coeffs[l + 1]));
                                        }

                                        for (int m = 0; m < 32; m++)
                                        {
                                            sampleBuffer[(k * 32 + m) * channels + ch] = (short)coeffs[m];
                                        }
                                    }
                                }

                                int samplesToWrite = samplesInBlock >= 128 ? 128 : (int)samplesInBlock;
                                if (samplesToWrite > 0)
                                {
                                    for (int n = 0; n < samplesToWrite * channels; n++)
                                    {
                                        bw.Write(sampleBuffer[n]);
                                    }
                                    samplesInBlock -= (uint)samplesToWrite;
                                }
                            }
                        }
                    }

                    if (fs.Position >= fs.Length)
                        break;

                    chunkCount++;
                }

                return true;
            }
            catch
            {
                return false;
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

            var snrFiles = Directory.GetFiles(directoryPath, "*.snr", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"\d+");
                    if (match.Success && int.TryParse(match.Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = snrFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var snrFilePath in snrFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(snrFilePath);
                    ConversionProgress?.Invoke(this, $"正在转换:{Path.GetFileName(snrFilePath)}");

                    string fileDirectory = Path.GetDirectoryName(snrFilePath) ?? string.Empty;
                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        bool conversionSuccess = await Task.Run(() => ConvertSnrToWav(snrFilePath, wavFilePath), cancellationToken);

                        if (conversionSuccess && File.Exists(wavFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"已转换:{Path.GetFileName(wavFilePath)}");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{Path.GetFileName(snrFilePath)}转换失败");
                            OnConversionFailed($"{Path.GetFileName(snrFilePath)}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}处理错误:{ex.Message}");
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