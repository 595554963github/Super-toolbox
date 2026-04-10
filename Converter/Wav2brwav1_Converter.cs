using System.Text;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2brwav1_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public enum BrwavEncoding
        {
            PCM8 = 0,
            PCM16 = 1,
            DSPADPCM = 2
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await ExtractAsyncInternal(directoryPath, BrwavEncoding.PCM8, cancellationToken);
        }

        protected async Task ExtractAsyncInternal(string directoryPath, BrwavEncoding encoding, CancellationToken cancellationToken)
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

                    try
                    {
                        string brwavFile = Path.Combine(fileDirectory, $"{fileName}.brwav");

                        if (File.Exists(brwavFile))
                            File.Delete(brwavFile);

                        bool conversionSuccess = await Task.Run(() => ConvertWavToBrwav(wavFilePath, brwavFile, encoding), cancellationToken);

                        if (conversionSuccess && File.Exists(brwavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(brwavFile)}");
                            OnFileConverted(brwavFile);
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

        private bool ConvertWavToBrwav(string wavFilePath, string brwavFilePath, BrwavEncoding encoding)
        {
            byte[] data = File.ReadAllBytes(wavFilePath);

            int channels = 0;
            int sampleRate = 0;
            int bitsPerSample = 0;
            int totalSamples = 0;
            uint dataChunkSize = 0;
            byte[] pcmData = Array.Empty<byte>();

            if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F')
                throw new Exception("Not a WAV file");

            int pos = 12;
            while (pos + 8 < data.Length)
            {
                string chunk = Encoding.ASCII.GetString(data, pos, 4);
                int size = BitConverter.ToInt32(data, pos + 4);

                if (chunk == "fmt ")
                {
                    int fmtPos = pos + 8;
                    int audioFormat = BitConverter.ToInt16(data, fmtPos);
                    channels = BitConverter.ToInt16(data, fmtPos + 2);
                    sampleRate = BitConverter.ToInt32(data, fmtPos + 4);
                    bitsPerSample = BitConverter.ToInt16(data, fmtPos + 14);

                    if (audioFormat != 1)
                        throw new Exception("Only PCM WAV supported");
                    if (bitsPerSample != 16)
                        throw new Exception("Only 16-bit PCM WAV supported");
                }
                else if (chunk == "data")
                {
                    dataChunkSize = (uint)size;
                    pcmData = new byte[size];
                    Array.Copy(data, pos + 8, pcmData, 0, size);
                    int bytesPerSample = bitsPerSample / 8;
                    totalSamples = size / (channels * bytesPerSample);
                    break;
                }

                pos += 8 + size;
            }

            if (pcmData.Length == 0)
                throw new Exception("No data chunk found");

            byte[] brwav = encoding switch
            {
                BrwavEncoding.PCM8 => BuildPCM8(channels, sampleRate, totalSamples, pcmData, dataChunkSize),
                BrwavEncoding.PCM16 => BuildPCM16(channels, sampleRate, totalSamples, pcmData, dataChunkSize),
                BrwavEncoding.DSPADPCM => BuildDSPADPCM(channels, sampleRate, totalSamples, pcmData, dataChunkSize),
                _ => throw new Exception("Unknown encoding")
            };
            File.WriteAllBytes(brwavFilePath, brwav);
            return true;
        }

        private static void WriteBigEndian(BinaryWriter w, int value)
        {
            w.Write((byte)((value >> 24) & 0xFF));
            w.Write((byte)((value >> 16) & 0xFF));
            w.Write((byte)((value >> 8) & 0xFF));
            w.Write((byte)(value & 0xFF));
        }

        private static void WriteBigEndian(BinaryWriter w, uint value)
        {
            w.Write((byte)((value >> 24) & 0xFF));
            w.Write((byte)((value >> 16) & 0xFF));
            w.Write((byte)((value >> 8) & 0xFF));
            w.Write((byte)(value & 0xFF));
        }

        private static void WriteBigEndian(BinaryWriter w, ushort value)
        {
            w.Write((byte)((value >> 8) & 0xFF));
            w.Write((byte)(value & 0xFF));
        }

        private static byte[] BuildPCM8(int channels, int sampleRate, int totalSamples, byte[] pcmData, uint dataChunkSize)
        {
            int dataSize = totalSamples * channels;
            int dataAligned = (dataSize + 0x1F) & ~0x1F;

            uint dataSectionSize = (uint)dataAligned + 8;

            int headerSize = 0x20;
            int infoHeaderSize = 8;
            int waveInfoSize = 0x1C;
            int channelTableSize = channels * 4;
            int channelInfoSize = 0x1C;

            int infoSectionActualSize = infoHeaderSize + waveInfoSize + channelTableSize + channels * channelInfoSize;

            int dataOffset = (headerSize + infoSectionActualSize + 0x1F) & ~0x1F;
            if (dataOffset < 0xA0) dataOffset = 0xA0;

            int totalSize = dataOffset + 8 + dataAligned;

            int nibbles = (int)Math.Round(dataChunkSize / 3.5);

            byte[] output = new byte[totalSize];
            using (MemoryStream ms = new MemoryStream(output))
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RWAV"));
                WriteBigEndian(w, (ushort)0xFEFF);
                WriteBigEndian(w, (ushort)0x0102);
                WriteBigEndian(w, totalSize);
                WriteBigEndian(w, (ushort)0x20);
                WriteBigEndian(w, (ushort)2);
                WriteBigEndian(w, headerSize);
                WriteBigEndian(w, infoSectionActualSize);
                WriteBigEndian(w, dataOffset);
                WriteBigEndian(w, dataSectionSize);

                w.Write(Encoding.ASCII.GetBytes("INFO"));
                WriteBigEndian(w, infoSectionActualSize);

                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((byte)channels);
                w.Write((byte)0);
                WriteBigEndian(w, (ushort)sampleRate);
                w.Write((byte)0);
                w.Write((byte)0);
                WriteBigEndian(w, 0);
                WriteBigEndian(w, nibbles);
                WriteBigEndian(w, 0x1C);
                WriteBigEndian(w, (uint)0x5C);
                WriteBigEndian(w, 0);

                int channelInfoOffset = 0x1C + channelTableSize;
                for (int i = 0; i < channels; i++)
                {
                    WriteBigEndian(w, channelInfoOffset + i * channelInfoSize);
                }

                int offsetPerChannel = dataAligned / channels;
                for (int i = 0; i < channels; i++)
                {
                    WriteBigEndian(w, i * offsetPerChannel);
                    WriteBigEndian(w, 0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    WriteBigEndian(w, 0);
                }

                int padding = dataOffset - (headerSize + infoSectionActualSize);
                if (padding > 0) w.Write(new byte[padding]);

                w.Write(Encoding.ASCII.GetBytes("DATA"));
                WriteBigEndian(w, dataSectionSize);

                for (int ch = 0; ch < channels; ch++)
                {
                    for (int i = 0; i < totalSamples; i++)
                    {
                        int srcPos = (i * channels + ch) * 2;
                        short sample16 = BitConverter.ToInt16(pcmData, srcPos);
                        sbyte sample8 = (sbyte)(sample16 >> 8);
                        w.Write((byte)sample8);
                    }
                }

                if (dataAligned > dataSize)
                    w.Write(new byte[dataAligned - dataSize]);
            }
            return output;
        }

        private static byte[] BuildPCM16(int channels, int sampleRate, int totalSamples, byte[] pcmData, uint dataChunkSize)
        {
            int bytesPerSample = 2;
            int dataSize = totalSamples * channels * bytesPerSample;
            int dataAligned = (dataSize + 0x1F) & ~0x1F;

            uint dataSectionSize = (uint)dataAligned + 8;

            int headerSize = 0x20;
            int infoHeaderSize = 8;
            int waveInfoSize = 0x1C;
            int channelTableSize = channels * 4;
            int channelInfoSize = 0x1C;

            int infoSectionActualSize = infoHeaderSize + waveInfoSize + channelTableSize + channels * channelInfoSize;

            int dataOffset = 0xA0;

            int totalSize = dataOffset + 8 + dataAligned;

            int nibbles = (int)Math.Round(dataChunkSize / 3.5);

            byte[] output = new byte[totalSize];
            using (MemoryStream ms = new MemoryStream(output))
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RWAV"));
                WriteBigEndian(w, (ushort)0xFEFF);
                WriteBigEndian(w, (ushort)0x0102);
                WriteBigEndian(w, totalSize);
                WriteBigEndian(w, (ushort)0x20);
                WriteBigEndian(w, (ushort)2);
                WriteBigEndian(w, headerSize);
                WriteBigEndian(w, infoSectionActualSize);
                WriteBigEndian(w, dataOffset);
                WriteBigEndian(w, dataSectionSize);

                w.Write(Encoding.ASCII.GetBytes("INFO"));
                WriteBigEndian(w, infoSectionActualSize);

                w.Write((byte)1);
                w.Write((byte)0);
                w.Write((byte)channels);
                w.Write((byte)0);
                WriteBigEndian(w, (ushort)sampleRate);
                w.Write((byte)0);
                w.Write((byte)0);
                WriteBigEndian(w, 0);
                WriteBigEndian(w, nibbles);
                WriteBigEndian(w, 0x1C);
                WriteBigEndian(w, (uint)0x5C);
                WriteBigEndian(w, 0);

                int channelInfoOffset = 0x1C + channelTableSize;
                for (int i = 0; i < channels; i++)
                {
                    WriteBigEndian(w, channelInfoOffset + i * channelInfoSize);
                }

                int offsetPerChannel = dataAligned / channels;
                for (int i = 0; i < channels; i++)
                {
                    WriteBigEndian(w, i * offsetPerChannel);
                    WriteBigEndian(w, 0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    WriteBigEndian(w, 0);
                }

                int padding = dataOffset - (headerSize + infoSectionActualSize);
                if (padding > 0) w.Write(new byte[padding]);

                w.Write(Encoding.ASCII.GetBytes("DATA"));
                WriteBigEndian(w, dataSectionSize);

                for (int ch = 0; ch < channels; ch++)
                {
                    for (int i = 0; i < totalSamples; i++)
                    {
                        int srcPos = (i * channels + ch) * 2;
                        short sample = BitConverter.ToInt16(pcmData, srcPos);
                        w.Write((byte)((sample >> 8) & 0xFF));
                        w.Write((byte)(sample & 0xFF));
                    }
                }

                if (dataAligned > dataSize)
                    w.Write(new byte[dataAligned - dataSize]);
            }
            return output;
        }

        private static byte[] BuildDSPADPCM(int channels, int sampleRate, int totalSamples, byte[] pcmData, uint dataChunkSize)
        {
            int samplesPerChannel = totalSamples;
            int framesPerChannel = (samplesPerChannel + 13) / 14;
            int blockLen = framesPerChannel * 8;
            int dataSize = blockLen * channels;
            int dataAligned = (dataSize + 0x1F) & ~0x1F;

            uint dataSectionSize = (uint)dataAligned + 8;

            int headerSize = 0x20;
            int infoHeaderSize = 8;
            int waveInfoSize = 0x1C;
            int channelTableSize = channels * 4;
            int channelInfoSize = 0x1C;
            int adpcmInfoSize = 0x30;

            int infoSectionActualSize = infoHeaderSize + waveInfoSize + channelTableSize + channels * channelInfoSize + channels * adpcmInfoSize;

            int dataOffset = (headerSize + infoSectionActualSize + 0x1F) & ~0x1F;
            if (dataOffset < 0xA0) dataOffset = 0xA0;

            int nibbles = (samplesPerChannel / 14) * 16;
            int extraSamples = samplesPerChannel % 14;
            if (extraSamples > 0)
                nibbles += extraSamples + 2;

            short[][] channelPcm = new short[channels][];
            for (int ch = 0; ch < channels; ch++)
            {
                channelPcm[ch] = new short[samplesPerChannel + 2];
                for (int i = 0; i < samplesPerChannel; i++)
                {
                    int srcPos = (i * channels + ch) * 2;
                    channelPcm[ch][i + 2] = (short)((pcmData[srcPos + 1] << 8) | pcmData[srcPos]);
                }
            }

            short[][][] adpcmCoefs = new short[channels][][];
            byte[][] encodedData = new byte[channels][];
            short[][][] encodedSamples = new short[channels][][];

            for (int ch = 0; ch < channels; ch++)
            {
                adpcmCoefs[ch] = new short[8][];
                for (int i = 0; i < 8; i++)
                    adpcmCoefs[ch][i] = new short[2];

                encodedData[ch] = new byte[blockLen];
                encodedSamples[ch] = new short[framesPerChannel][];
                for (int f = 0; f < framesPerChannel; f++)
                    encodedSamples[ch][f] = new short[16];
            }

            for (int ch = 0; ch < channels; ch++)
            {
                CalculateAdpcmCoefs(channelPcm[ch], 2, samplesPerChannel, adpcmCoefs[ch]);

                short[] pcmFrame = new short[16];
                byte[] adpcmFrame = new byte[8];

                pcmFrame[0] = 0;
                pcmFrame[1] = 0;

                for (int frame = 0; frame < framesPerChannel; frame++)
                {
                    int frameStart = frame * 14;
                    int samplesInFrame = Math.Min(14, samplesPerChannel - frameStart);

                    for (int i = 0; i < samplesInFrame; i++)
                        pcmFrame[i + 2] = channelPcm[ch][frameStart + i + 2];
                    for (int i = samplesInFrame + 2; i < 16; i++)
                        pcmFrame[i] = 0;

                    EncodeDspFrame(pcmFrame, 14, adpcmFrame, adpcmCoefs[ch]);

                    for (int i = 0; i < 16; i++)
                        encodedSamples[ch][frame][i] = pcmFrame[i];

                    pcmFrame[0] = pcmFrame[14];
                    pcmFrame[1] = pcmFrame[15];

                    int bytesToWrite = (samplesInFrame + 1) / 2 + 1;
                    Array.Copy(adpcmFrame, 0, encodedData[ch], frame * 8, bytesToWrite);
                }
            }

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RWAV"));
                WriteBigEndian(w, (ushort)0xFEFF);
                WriteBigEndian(w, (ushort)0x0102);
                WriteBigEndian(w, 0);
                WriteBigEndian(w, (ushort)0x20);
                WriteBigEndian(w, (ushort)2);
                WriteBigEndian(w, headerSize);
                WriteBigEndian(w, infoSectionActualSize);
                WriteBigEndian(w, dataOffset);
                WriteBigEndian(w, dataSectionSize);

                w.Write(Encoding.ASCII.GetBytes("INFO"));
                WriteBigEndian(w, infoSectionActualSize);

                w.Write((byte)2);
                w.Write((byte)0);
                w.Write((byte)channels);
                w.Write((byte)0);
                WriteBigEndian(w, (ushort)sampleRate);
                w.Write((byte)0);
                w.Write((byte)0);
                WriteBigEndian(w, 2);
                WriteBigEndian(w, nibbles);
                WriteBigEndian(w, 0x1C);
                WriteBigEndian(w, (uint)0xBC);
                WriteBigEndian(w, 0);

                int channelInfoOffset = 0x1C + channelTableSize;
                int adpcmInfoOffset = channelInfoOffset + channels * channelInfoSize;

                for (int i = 0; i < channels; i++)
                {
                    WriteBigEndian(w, channelInfoOffset + i * channelInfoSize);
                }

                for (int i = 0; i < channels; i++)
                {
                    WriteBigEndian(w, i * blockLen);
                    WriteBigEndian(w, adpcmInfoOffset + i * adpcmInfoSize);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    WriteBigEndian(w, 0);
                }

                for (int ch = 0; ch < channels; ch++)
                {
                    byte[] adpcmInfoBytes = new byte[0x30];
                    for (int i = 0; i < 8; i++)
                    {
                        adpcmInfoBytes[i * 4] = (byte)(adpcmCoefs[ch][i][0] >> 8);
                        adpcmInfoBytes[i * 4 + 1] = (byte)(adpcmCoefs[ch][i][0] & 0xFF);
                        adpcmInfoBytes[i * 4 + 2] = (byte)(adpcmCoefs[ch][i][1] >> 8);
                        adpcmInfoBytes[i * 4 + 3] = (byte)(adpcmCoefs[ch][i][1] & 0xFF);
                    }

                    adpcmInfoBytes[0x20] = 0;
                    adpcmInfoBytes[0x21] = 0;
                    adpcmInfoBytes[0x22] = (byte)(encodedData[ch][0] >> 8);
                    adpcmInfoBytes[0x23] = (byte)(encodedData[ch][0] & 0xFF);
                    adpcmInfoBytes[0x24] = (byte)(encodedSamples[ch][0][15] >> 8);
                    adpcmInfoBytes[0x25] = (byte)(encodedSamples[ch][0][15] & 0xFF);
                    adpcmInfoBytes[0x26] = (byte)(encodedSamples[ch][0][14] >> 8);
                    adpcmInfoBytes[0x27] = (byte)(encodedSamples[ch][0][14] & 0xFF);
                    adpcmInfoBytes[0x28] = 0;
                    adpcmInfoBytes[0x29] = 0;
                    adpcmInfoBytes[0x2A] = 0;
                    adpcmInfoBytes[0x2B] = 0;
                    adpcmInfoBytes[0x2C] = 0;
                    adpcmInfoBytes[0x2D] = 0;
                    adpcmInfoBytes[0x2E] = 0;
                    adpcmInfoBytes[0x2F] = 0;

                    w.Write(adpcmInfoBytes);
                }

                int padding = dataOffset - (headerSize + infoSectionActualSize);
                if (padding > 0)
                    w.Write(new byte[padding]);

                w.Write(Encoding.ASCII.GetBytes("DATA"));
                WriteBigEndian(w, dataSectionSize);

                for (int ch = 0; ch < channels; ch++)
                {
                    w.Write(encodedData[ch]);
                }

                if (dataAligned > dataSize)
                    w.Write(new byte[dataAligned - dataSize]);

                long finalSize = ms.Length;
                ms.Seek(0x08, SeekOrigin.Begin);
                WriteBigEndian(w, (int)finalSize);

                return ms.ToArray();
            }
        }

        private static void CalculateAdpcmCoefs(short[] pcm, int startIndex, int sampleCount, short[][] coefsOut)
        {
            double[,] autocorr = new double[3, 3];
            double[] r = new double[3];

            for (int i = 0; i < sampleCount; i++)
            {
                double x0 = pcm[startIndex + i];
                for (int j = 0; j <= 2; j++)
                {
                    r[j] += x0 * pcm[startIndex + i - j];
                    for (int k = 0; k <= 2; k++)
                        autocorr[j, k] += pcm[startIndex + i - j] * pcm[startIndex + i - k];
                }
            }

            double[,] a = new double[3, 3];
            double[] b = new double[3];

            for (int i = 1; i <= 2; i++)
            {
                b[i] = -r[i];
                for (int j = 1; j <= 2; j++)
                    a[i, j] = autocorr[i, j];
            }

            double[] coef = new double[3];
            coef[0] = 1.0;

            for (int i = 1; i <= 2; i++)
            {
                double sum = b[i];
                for (int j = 1; j < i; j++)
                    sum -= a[i, j] * coef[j];
                coef[i] = sum / a[i, i];
            }

            for (int i = 0; i < 8; i++)
            {
                coefsOut[i][0] = (short)Math.Round(-coef[1] * 2048.0);
                coefsOut[i][1] = (short)Math.Round(-coef[2] * 2048.0);

                if (coefsOut[i][0] > 32767) coefsOut[i][0] = 32767;
                if (coefsOut[i][0] < -32768) coefsOut[i][0] = -32768;
                if (coefsOut[i][1] > 32767) coefsOut[i][1] = 32767;
                if (coefsOut[i][1] < -32768) coefsOut[i][1] = -32768;
            }
        }

        private static void EncodeDspFrame(short[] pcmInOut, int sampleCount, byte[] adpcmOut, short[][] coefs)
        {
            int[,] inSamples = new int[8, 16];
            int[,] outSamples = new int[8, 14];
            int[] scale = new int[8];
            double[] distAccum = new double[8];
            int bestIndex = 0;

            for (int i = 0; i < 8; i++)
            {
                inSamples[i, 0] = pcmInOut[0];
                inSamples[i, 1] = pcmInOut[1];

                int distance = 0;
                for (int s = 0; s < sampleCount; s++)
                {
                    int v1 = (pcmInOut[s] * coefs[i][1] + pcmInOut[s + 1] * coefs[i][0]) / 2048;
                    int v2 = pcmInOut[s + 2] - v1;
                    int v3 = v2 > 32767 ? 32767 : (v2 < -32768 ? -32768 : v2);
                    if (Math.Abs(v3) > Math.Abs(distance))
                        distance = v3;
                }

                scale[i] = 0;
                while (scale[i] <= 12 && (distance > 7 || distance < -8))
                {
                    distance /= 2;
                    scale[i]++;
                }
                scale[i] = scale[i] <= 1 ? -1 : scale[i] - 2;

                int maxDelta;
                do
                {
                    scale[i]++;
                    distAccum[i] = 0;
                    maxDelta = 0;

                    for (int s = 0; s < sampleCount; s++)
                    {
                        int v1 = (inSamples[i, s] * coefs[i][1] + inSamples[i, s + 1] * coefs[i][0]);
                        int v2 = (pcmInOut[s + 2] << 11) - v1;
                        int v3 = v2 > 0 ? (int)((double)v2 / (1 << scale[i]) / 2048 + 0.5) : (int)((double)v2 / (1 << scale[i]) / 2048 - 0.5);

                        if (v3 < -8)
                        {
                            int overflow = -8 - v3;
                            if (overflow > maxDelta) maxDelta = overflow;
                            v3 = -8;
                        }
                        else if (v3 > 7)
                        {
                            int overflow = v3 - 7;
                            if (overflow > maxDelta) maxDelta = overflow;
                            v3 = 7;
                        }

                        outSamples[i, s] = v3;

                        v1 = (v1 + ((v3 * (1 << scale[i])) << 11) + 1024) >> 11;
                        int v4 = v1 > 32767 ? 32767 : (v1 < -32768 ? -32768 : v1);
                        inSamples[i, s + 2] = v4;

                        long diff = pcmInOut[s + 2] - v4;
                        distAccum[i] += diff * diff;
                    }

                    int temp = maxDelta + 8;
                    while (temp > 256)
                    {
                        temp >>= 1;
                        if (++scale[i] >= 12)
                        {
                            scale[i] = 11;
                            break;
                        }
                    }
                } while (scale[i] < 12 && maxDelta > 1);
            }

            double minDist = double.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                if (distAccum[i] < minDist)
                {
                    minDist = distAccum[i];
                    bestIndex = i;
                }
            }

            for (int s = 0; s < sampleCount; s++)
                pcmInOut[s + 2] = (short)inSamples[bestIndex, s + 2];

            adpcmOut[0] = (byte)((bestIndex << 4) | (scale[bestIndex] & 0x0F));

            for (int y = 0; y < 7; y++)
            {
                int sample1 = y * 2 < sampleCount ? outSamples[bestIndex, y * 2] : 0;
                int sample2 = y * 2 + 1 < sampleCount ? outSamples[bestIndex, y * 2 + 1] : 0;
                adpcmOut[y + 1] = (byte)((sample1 << 4) | (sample2 & 0x0F));
            }
        }
    }
}