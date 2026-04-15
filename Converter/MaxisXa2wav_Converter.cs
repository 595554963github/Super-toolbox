using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class MaxisXa2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static readonly int[] EATable = new int[]
        {
            0x00000000, 0x000000F0, 0x000001CC, 0x00000188,
            0x00000000, 0x00000000, -0x000000D0, -0x000000DC,
            0x00000000, 0x00000001, 0x00000003, 0x00000004,
            0x00000007, 0x00000008, 0x0000000A, 0x0000000B,
            0x00000000, -0x00000001, -0x00000003, -0x00000004
        };

        private static int Clip16BitSample(int sample)
        {
            if (sample > 32767) return 32767;
            if (sample < -32768) return -32768;
            return sample;
        }

        private static int GetHighNibble(byte b)
        {
            return (b >> 4) & 0x0F;
        }

        private static int GetLowNibble(byte b)
        {
            return b & 0x0F;
        }

        private static bool ConvertXaToWav(string xaFilePath, string wavFilePath)
        {
            try
            {
                using var xaFile = File.OpenRead(xaFilePath);
                using var wavFile = File.Create(wavFilePath);

                byte[] szID = new byte[4];
                xaFile.Read(szID, 0, 4);
                if (szID[0] != 'X' || (szID[1] != 'A' && szID[1] != 'I' && szID[1] != 'J'))
                {
                    return false;
                }

                byte[] dwOutSizeBytes = new byte[4];
                xaFile.Read(dwOutSizeBytes, 0, 4);
                uint dwOutSize = BitConverter.ToUInt32(dwOutSizeBytes, 0);

                byte[] wTagBytes = new byte[2];
                xaFile.Read(wTagBytes, 0, 2);
                ushort wTag = BitConverter.ToUInt16(wTagBytes, 0);
                if (wTag != 1) return false;

                byte[] wChannelsBytes = new byte[2];
                xaFile.Read(wChannelsBytes, 0, 2);
                ushort wChannels = BitConverter.ToUInt16(wChannelsBytes, 0);
                if (wChannels == 0 || wChannels > 2) return false;

                byte[] dwSampleRateBytes = new byte[4];
                xaFile.Read(dwSampleRateBytes, 0, 4);
                uint dwSampleRate = BitConverter.ToUInt32(dwSampleRateBytes, 0);

                byte[] dwAvgByteRateBytes = new byte[4];
                xaFile.Read(dwAvgByteRateBytes, 0, 4);
                uint dwAvgByteRate = BitConverter.ToUInt32(dwAvgByteRateBytes, 0);

                byte[] wAlignBytes = new byte[2];
                xaFile.Read(wAlignBytes, 0, 2);
                ushort wAlign = BitConverter.ToUInt16(wAlignBytes, 0);

                byte[] wBitsBytes = new byte[2];
                xaFile.Read(wBitsBytes, 0, 2);
                ushort wBits = BitConverter.ToUInt16(wBitsBytes, 0);
                if (wBits != 16) return false;

                uint nSamples = dwOutSize / 2 / wChannels;
                uint wavDataSize = nSamples * wChannels * 2;
                uint riffSize = 36 + wavDataSize;

                wavFile.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                wavFile.Write(BitConverter.GetBytes(riffSize));
                wavFile.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                wavFile.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                wavFile.Write(BitConverter.GetBytes(16));
                wavFile.Write(BitConverter.GetBytes((ushort)1));
                wavFile.Write(BitConverter.GetBytes(wChannels));
                wavFile.Write(BitConverter.GetBytes(dwSampleRate));
                wavFile.Write(BitConverter.GetBytes(dwAvgByteRate));
                wavFile.Write(BitConverter.GetBytes(wAlign));
                wavFile.Write(BitConverter.GetBytes(wBits));
                wavFile.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                wavFile.Write(BitConverter.GetBytes(wavDataSize));

                int lCurSampleLeft = 0, lPrevSampleLeft = 0;
                int lCurSampleRight = 0, lPrevSampleRight = 0;
                uint totalSamplesWritten = 0;

                if (wChannels == 1)
                {
                    byte[] block = new byte[0xF];
                    while (totalSamplesWritten < nSamples)
                    {
                        int bytesRead = xaFile.Read(block, 0, 0xF);
                        if (bytesRead < 0xF) break;

                        byte bInput = block[0];
                        int c1 = EATable[GetHighNibble(bInput)];
                        int c2 = EATable[GetHighNibble(bInput) + 4];
                        int d = GetLowNibble(bInput) + 8;

                        for (int i = 1; i < 0xF; i++)
                        {
                            int left = GetHighNibble(block[i]);
                            left = (left << 28) >> d;
                            left = (left + lCurSampleLeft * c1 + lPrevSampleLeft * c2 + 0x80) >> 8;
                            left = Clip16BitSample(left);
                            lPrevSampleLeft = lCurSampleLeft;
                            lCurSampleLeft = left;

                            if (totalSamplesWritten < nSamples)
                            {
                                short sample = (short)lCurSampleLeft;
                                wavFile.Write(BitConverter.GetBytes(sample));
                                totalSamplesWritten++;
                            }

                            left = GetLowNibble(block[i]);
                            left = (left << 28) >> d;
                            left = (left + lCurSampleLeft * c1 + lPrevSampleLeft * c2 + 0x80) >> 8;
                            left = Clip16BitSample(left);
                            lPrevSampleLeft = lCurSampleLeft;
                            lCurSampleLeft = left;

                            if (totalSamplesWritten < nSamples)
                            {
                                short sample = (short)lCurSampleLeft;
                                wavFile.Write(BitConverter.GetBytes(sample));
                                totalSamplesWritten++;
                            }
                        }
                    }
                }
                else
                {
                    byte[] block = new byte[0x1E];
                    while (totalSamplesWritten < nSamples)
                    {
                        int bytesRead = xaFile.Read(block, 0, 0x1E);
                        if (bytesRead < 0x1E) break;

                        byte bInput = block[0];
                        int c1left = EATable[GetHighNibble(bInput)];
                        int c2left = EATable[GetHighNibble(bInput) + 4];
                        int dleft = GetLowNibble(bInput) + 8;

                        bInput = block[1];
                        int c1right = EATable[GetHighNibble(bInput)];
                        int c2right = EATable[GetHighNibble(bInput) + 4];
                        int dright = GetLowNibble(bInput) + 8;

                        for (int i = 2; i < 0x1E; i += 2)
                        {
                            int left = GetHighNibble(block[i]);
                            left = (left << 28) >> dleft;
                            left = (left + lCurSampleLeft * c1left + lPrevSampleLeft * c2left + 0x80) >> 8;
                            left = Clip16BitSample(left);
                            lPrevSampleLeft = lCurSampleLeft;
                            lCurSampleLeft = left;

                            int right = GetHighNibble(block[i + 1]);
                            right = (right << 28) >> dright;
                            right = (right + lCurSampleRight * c1right + lPrevSampleRight * c2right + 0x80) >> 8;
                            right = Clip16BitSample(right);
                            lPrevSampleRight = lCurSampleRight;
                            lCurSampleRight = right;

                            if (totalSamplesWritten < nSamples)
                            {
                                short sampleL = (short)lCurSampleLeft;
                                short sampleR = (short)lCurSampleRight;
                                wavFile.Write(BitConverter.GetBytes(sampleL));
                                wavFile.Write(BitConverter.GetBytes(sampleR));
                                totalSamplesWritten++;
                            }

                            left = GetLowNibble(block[i]);
                            left = (left << 28) >> dleft;
                            left = (left + lCurSampleLeft * c1left + lPrevSampleLeft * c2left + 0x80) >> 8;
                            left = Clip16BitSample(left);
                            lPrevSampleLeft = lCurSampleLeft;
                            lCurSampleLeft = left;

                            right = GetLowNibble(block[i + 1]);
                            right = (right << 28) >> dright;
                            right = (right + lCurSampleRight * c1right + lPrevSampleRight * c2right + 0x80) >> 8;
                            right = Clip16BitSample(right);
                            lPrevSampleRight = lCurSampleRight;
                            lCurSampleRight = right;

                            if (totalSamplesWritten < nSamples)
                            {
                                short sampleL = (short)lCurSampleLeft;
                                short sampleR = (short)lCurSampleRight;
                                wavFile.Write(BitConverter.GetBytes(sampleL));
                                wavFile.Write(BitConverter.GetBytes(sampleR));
                                totalSamplesWritten++;
                            }
                        }
                    }
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

            var xaFiles = Directory.GetFiles(directoryPath, "*.xa", SearchOption.AllDirectories)
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

            TotalFilesToConvert = xaFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var xaFilePath in xaFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(xaFilePath);
                    ConversionProgress?.Invoke(this, $"正在转换:{fileName}.xa");

                    string fileDirectory = Path.GetDirectoryName(xaFilePath) ?? string.Empty;
                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        bool conversionSuccess = await Task.Run(() => ConvertXaToWav(xaFilePath, wavFilePath), cancellationToken);

                        if (conversionSuccess && File.Exists(wavFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"已转换:{Path.GetFileName(wavFilePath)}");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.xa转换失败");
                            OnConversionFailed($"{fileName}.xa转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.xa处理错误:{ex.Message}");
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