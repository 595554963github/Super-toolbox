using System.Text;
using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Aifc2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var aifcFiles = Directory.GetFiles(directoryPath, "*.aifc", SearchOption.AllDirectories)
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

            TotalFilesToConvert = aifcFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var aifcFilePath in aifcFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(aifcFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.aifc");

                    string fileDirectory = Path.GetDirectoryName(aifcFilePath) ?? string.Empty;
                    string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavFile))
                        {
                            File.Delete(wavFile);
                        }

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertAifcToWav(aifcFilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.aifc转换失败");
                            OnConversionFailed($"{fileName}.aifc转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.aifc处理错误:{ex.Message}");
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

        private bool ConvertAifcToWav(string aifcFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                using (var fs = File.OpenRead(aifcFilePath))
                using (var reader = new BinaryReader(fs))
                {
                    string formType = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (formType != "FORM")
                    {
                        throw new InvalidOperationException("不是有效的AIFC文件");
                    }

                    int formSize = FromBigEndianInt32(reader.ReadBytes(4));
                    string aifcType = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (aifcType != "AIFC")
                    {
                        throw new InvalidOperationException("不是有效的AIFC文件");
                    }

                    int channels = 0;
                    int samples = 0;
                    int sampleSize = 0;
                    int sampleRate = 0;
                    string compressionType = "";
                    byte[]? pcmData = null;

                    long endPosition = fs.Position + formSize;
                    bool commChunkFound = false;
                    bool ssndChunkFound = false;

                    while (fs.Position < endPosition)
                    {
                        string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        int chunkSize = FromBigEndianInt32(reader.ReadBytes(4));
                        long chunkStart = fs.Position;

                        if (chunkId == "COMM")
                        {
                            channels = FromBigEndianInt16(reader.ReadBytes(2));
                            samples = FromBigEndianInt32(reader.ReadBytes(4));
                            sampleSize = FromBigEndianInt16(reader.ReadBytes(2));

                            ushort commExponent = FromBigEndianUInt16(reader.ReadBytes(2));
                            ulong commMantissa = FromBigEndianUInt64(reader.ReadBytes(8));
                            sampleRate = ConvertExtended80ToInt(commExponent, commMantissa);

                            compressionType = Encoding.ASCII.GetString(reader.ReadBytes(4));

                            if (compressionType != "NONE" && compressionType != "sowt")
                            {
                                throw new InvalidOperationException($"不支持的压缩格式: {compressionType}");
                            }

                            commChunkFound = true;
                        }
                        else if (chunkId == "SSND")
                        {
                            int offset = FromBigEndianInt32(reader.ReadBytes(4));
                            int blockSize = FromBigEndianInt32(reader.ReadBytes(4));

                            if (offset > 0)
                            {
                                reader.ReadBytes(offset);
                            }

                            int bytesPerSample = sampleSize / 8;
                            int dataSize = samples * channels * bytesPerSample;
                            pcmData = reader.ReadBytes(dataSize);

                            ssndChunkFound = true;
                        }

                        fs.Seek(chunkStart + chunkSize, SeekOrigin.Begin);
                        if (chunkSize % 2 != 0)
                        {
                            fs.ReadByte();
                        }
                    }

                    if (!commChunkFound || !ssndChunkFound || pcmData == null)
                    {
                        throw new InvalidOperationException("AIFC文件缺少必要的chunk");
                    }

                    short[][] channelsData = new short[channels][];
                    for (int c = 0; c < channels; c++)
                    {
                        channelsData[c] = new short[samples];
                    }

                    int bytesPerSampleVal = sampleSize / 8;

                    if (compressionType == "sowt")
                    {
                        for (int s = 0; s < samples; s++)
                        {
                            for (int c = 0; c < channels; c++)
                            {
                                int offset = (s * channels + c) * bytesPerSampleVal;
                                short value = (short)((pcmData[offset + 1] << 8) | pcmData[offset]);
                                channelsData[c][s] = value;
                            }
                        }
                    }
                    else
                    {
                        for (int s = 0; s < samples; s++)
                        {
                            for (int c = 0; c < channels; c++)
                            {
                                int offset = (s * channels + c) * bytesPerSampleVal;
                                short value = (short)((pcmData[offset] << 8) | pcmData[offset + 1]);
                                channelsData[c][s] = value;
                            }
                        }
                    }

                    var pcm16Format = new Pcm16Format(channelsData, sampleRate);
                    var waveWriter = new WaveWriter();

                    using (var wavStream = File.Create(wavFilePath))
                    {
                        waveWriter.WriteToStream(pcm16Format, wavStream);
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

        private int FromBigEndianInt32(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToInt32(bytes, 0);
        }

        private short FromBigEndianInt16(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToInt16(bytes, 0);
        }

        private ushort FromBigEndianUInt16(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt16(bytes, 0);
        }

        private ulong FromBigEndianUInt64(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt64(bytes, 0);
        }

        private int ConvertExtended80ToInt(ushort exponent, ulong mantissa)
        {
            if (exponent == 0 && mantissa == 0)
            {
                return 0;
            }

            int ieeeExponent = exponent - 16383 + 1023;
            ulong ieeeMantissa = (mantissa & 0x7FFFFFFFFFFFFFFF) >> 11;

            ulong ieeeBits = ((ulong)ieeeExponent << 52) | ieeeMantissa;
            double value = BitConverter.Int64BitsToDouble((long)ieeeBits);

            return (int)Math.Round(value);
        }
    }
}