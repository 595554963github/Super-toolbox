using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Sony_psxadpcm2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static readonly float[,] AdpcmCoef = new float[5, 2]
        {
            { 0.0f, 0.0f },
            { 0.9375f, 0.0f },
            { 1.796875f, -0.8125f },
            { 1.53125f, -0.859375f },
            { 1.90625f, -0.9375f }
        };

        private static readonly int[] NibbleToInt = new int[16]
        {
            0, 1, 2, 3, 4, 5, 6, 7, -8, -7, -6, -5, -4, -3, -2, -1
        };

        private static int GetHighNibbleSigned(byte n)
        {
            return NibbleToInt[n >> 4];
        }

        private static int GetLowNibbleSigned(byte n)
        {
            return NibbleToInt[n & 0xF];
        }

        private static short Clamp16(int n)
        {
            if (n > 0x7FFF) return 0x7FFF;
            if (n < -0x8000) return -0x8000;
            return (short)n;
        }

        private static void DecodeAdpcmBlock(byte[] frame, short[] outbuf, int offset, ref short old, ref short older)
        {
            byte coefIndex = (byte)((frame[0] >> 4) & 0xF);
            byte shiftFactor = (byte)(frame[0] & 0xF);

            if (coefIndex > 5) coefIndex = 4;
            if (shiftFactor > 12) shiftFactor = 9;

            shiftFactor = (byte)(20 - shiftFactor);

            for (int index = 0; index < 28; index++)
            {
                byte nibbles = frame[2 + index / 2];
                int sample;

                if ((index & 1) == 1)
                    sample = GetHighNibbleSigned(nibbles) << shiftFactor;
                else
                    sample = GetLowNibbleSigned(nibbles) << shiftFactor;

                sample += (int)(AdpcmCoef[coefIndex, 0] * old + AdpcmCoef[coefIndex, 1] * older) * 256;
                sample >>= 8;

                outbuf[offset + index] = Clamp16(sample);

                older = old;
                old = (short)sample;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WavHeader
        {
            public byte[] RiffIdentifier;
            public uint FileSize;
            public byte[] WaveIdentifier;
            public byte[] FmtIdentifier;
            public uint FmtChunkSize;
            public ushort AudioFormat;
            public ushort NumberChannels;
            public uint Frequency;
            public uint BytePerSec;
            public ushort BytePerBlock;
            public ushort BitsPerSample;
            public byte[] DataIdentifier;
            public uint DataSize;

            public static WavHeader Create(int channels, int frequency, int dataSize)
            {
                WavHeader header = new WavHeader();
                header.RiffIdentifier = System.Text.Encoding.ASCII.GetBytes("RIFF");
                header.WaveIdentifier = System.Text.Encoding.ASCII.GetBytes("WAVE");
                header.FmtIdentifier = System.Text.Encoding.ASCII.GetBytes("fmt ");
                header.DataIdentifier = System.Text.Encoding.ASCII.GetBytes("data");
                header.FmtChunkSize = 16;
                header.AudioFormat = 1;
                header.NumberChannels = (ushort)channels;
                header.Frequency = (uint)frequency;
                header.BitsPerSample = 16;
                header.BytePerBlock = (ushort)(channels * header.BitsPerSample / 8);
                header.BytePerSec = (uint)(frequency * header.BytePerBlock);
                header.DataSize = (uint)dataSize;
                header.FileSize = (uint)(dataSize + System.Runtime.InteropServices.Marshal.SizeOf<WavHeader>() - 8);
                return header;
            }

            public byte[] ToBytes()
            {
                byte[] bytes = new byte[44];
                Buffer.BlockCopy(RiffIdentifier, 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(FileSize), 0, bytes, 4, 4);
                Buffer.BlockCopy(WaveIdentifier, 0, bytes, 8, 4);
                Buffer.BlockCopy(FmtIdentifier, 0, bytes, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(FmtChunkSize), 0, bytes, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(AudioFormat), 0, bytes, 20, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NumberChannels), 0, bytes, 22, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Frequency), 0, bytes, 24, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(BytePerSec), 0, bytes, 28, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(BytePerBlock), 0, bytes, 32, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(BitsPerSample), 0, bytes, 34, 2);
                Buffer.BlockCopy(DataIdentifier, 0, bytes, 36, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(DataSize), 0, bytes, 40, 4);
                return bytes;
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

            var pcmFiles = Directory.GetFiles(directoryPath, "*.pcm", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"(\d+)$");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = pcmFiles.Length;
            int successCount = 0;
            int sampleRate = 44100;

            try
            {
                foreach (var pcmFilePath in pcmFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(pcmFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.pcm");

                    string fileDirectory = Path.GetDirectoryName(pcmFilePath) ?? string.Empty;
                    string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertPcmToWav(pcmFilePath, wavFile, sampleRate, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.pcm转换失败");
                            OnConversionFailed($"{fileName}.pcm转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.pcm处理错误:{ex.Message}");
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

        private bool ConvertPcmToWav(string pcmFilePath, string wavFilePath, int sampleRate, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"解码PCM文件:{Path.GetFileName(pcmFilePath)}");

                using (FileStream fsInput = new FileStream(pcmFilePath, FileMode.Open, FileAccess.Read))
                using (FileStream fsOutput = new FileStream(wavFilePath, FileMode.Create, FileAccess.Write))
                {
                    long fileSize = fsInput.Length;
                    int sourceChannels = 1;
                    int targetChannels = 2;
                    int interleave = 16;
                    int bytesPerChunk = interleave * sourceChannels;

                    int numChunks = (int)(fileSize / bytesPerChunk);

                    if (numChunks == 0)
                    {
                        throw new Exception("文件太小或格式不正确");
                    }

                    long remainingBytes = fileSize % bytesPerChunk;
                    if (remainingBytes > 0)
                    {
                        ConversionProgress?.Invoke(this, $"警告:文件末尾有{remainingBytes}字节不完整数据,将被忽略");
                    }

                    int outChannelLength = interleave / 16 * 28;
                    int totalSamplesPerChannel = numChunks * outChannelLength;
                    int totalSamples = totalSamplesPerChannel * targetChannels;
                    int dataBytes = totalSamples * sizeof(short);

                    WavHeader header = WavHeader.Create(targetChannels, sampleRate, dataBytes);
                    byte[] headerBytes = header.ToBytes();
                    fsOutput.Write(headerBytes, 0, headerBytes.Length);

                    byte[] inChunk = new byte[bytesPerChunk];
                    short[] decodedMono = new short[outChannelLength];
                    short[] stereoOut = new short[outChannelLength * 2];
                    short old = 0;
                    short older = 0;

                    for (int chunk = 0; chunk < numChunks; chunk++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int bytesRead = fsInput.Read(inChunk, 0, bytesPerChunk);
                        if (bytesRead < bytesPerChunk) break;

                        for (int sampleIndex = 0; sampleIndex < interleave / 16; sampleIndex++)
                        {
                            byte[] frame = new byte[16];
                            Array.Copy(inChunk, sampleIndex * 16, frame, 0, 16);
                            DecodeAdpcmBlock(frame, decodedMono, sampleIndex * 28, ref old, ref older);
                        }

                        for (int i = 0; i < outChannelLength; i++)
                        {
                            stereoOut[i * 2] = decodedMono[i];
                            stereoOut[i * 2 + 1] = decodedMono[i];
                        }

                        byte[] outBytes = new byte[stereoOut.Length * sizeof(short)];
                        Buffer.BlockCopy(stereoOut, 0, outBytes, 0, outBytes.Length);
                        fsOutput.Write(outBytes, 0, outBytes.Length);
                    }
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
    }
}