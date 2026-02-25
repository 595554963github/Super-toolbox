using VGAudio.Formats;
using VGAudio.Formats.Pcm16;
using VGAudio.Containers.Wave;

namespace super_toolbox
{
    public class Wav2vag_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        public int ResamplingType { get; set; } = 2;
        public double ResampleFactor { get; set; } = 1.0;
        public string? TrackName { get; set; }
        public bool EnableLooping { get; set; } = false;

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
                var match = System.Text.RegularExpressions.Regex.Match(fileName, @"_(\d+)$");
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
                        var loopPoints = ParseLoopPoints(fileName);
                        bool conversionSuccess = await ConvertWavToVag(
                            wavFilePath,
                            fileDirectory,
                            fileName,
                            loopPoints.loopStart,
                            loopPoints.loopEnd,
                            cancellationToken);

                        if (conversionSuccess)
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{fileName}");
                            OnFileConverted(Path.Combine(fileDirectory, $"{fileName}.vag"));
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

        private async Task<bool> ConvertWavToVag(
            string wavFilePath,
            string outputDir,
            string fileName,
            int loopStart,
            int loopEnd,
            CancellationToken cancellationToken)
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
                    throw new InvalidOperationException("无法读取WAV音频数据");
                }

                var pcmFormat = audioData.GetFormat<Pcm16Format>();
                int sampleRate = pcmFormat.SampleRate;

                var monoPcm = MixToMono(pcmFormat);
                string vagFile = Path.Combine(outputDir, $"{fileName}.vag");
                EncodeMonoToVag(monoPcm, vagFile, loopStart, loopEnd, sampleRate);

                return true;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }

        private Pcm16Format MixToMono(Pcm16Format pcmFormat)
        {
            short[][] channels = pcmFormat.Channels;
            int channelCount = channels.Length;
            int sampleCount = channels[0].Length;
            short[] mono = new short[sampleCount];

            if (channelCount == 1)
            {
                return pcmFormat;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                int sum = 0;
                for (int ch = 0; ch < channelCount; ch++)
                {
                    sum += channels[ch][i];
                }
                mono[i] = (short)(sum / channelCount);
            }

            return new Pcm16Format(new short[][] { mono }, pcmFormat.SampleRate);
        }

        private void EncodeMonoToVag(Pcm16Format pcmFormat, string vagFile, int loopStart, int loopEnd, int sampleRate)
        {
            short[] samples = pcmFormat.Channels[0];

            if (ResampleFactor != 1.0)
            {
                samples = ResampleSamples(samples, ResampleFactor, ResamplingType);
            }

            byte[] vagData = EncodeAdpcmToVag(samples, loopStart, loopEnd, TrackName, sampleRate);
            File.WriteAllBytes(vagFile, vagData);
        }

        private byte[] EncodeAdpcmToVag(short[] samples, int loopStart, int loopEnd, string? trackName, int sampleRate)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            WriteUint32BE(writer, 0x56414770);
            WriteUint32BE(writer, 0x20);
            WriteUint32BE(writer, 0x00);

            int frameCount = (samples.Length + 27) / 28;
            int dataSize = 16 * (frameCount + 2);
            WriteUint32BE(writer, (uint)dataSize);

            WriteUint32BE(writer, (uint)sampleRate);
            writer.Write(new byte[12]);

            byte[] nameBytes = new byte[16];
            if (!string.IsNullOrEmpty(trackName))
            {
                byte[] trackBytes = System.Text.Encoding.ASCII.GetBytes(trackName);
                Array.Copy(trackBytes, nameBytes, Math.Min(trackBytes.Length, 16));
            }
            writer.Write(nameBytes);
            writer.Write(new byte[16]);

            int[] lpcTap = new int[2] { 0, 0 };
            int flags = EnableLooping ? 6 : 0;

            int samplesProcessed = 0;
            int totalSamples = samples.Length;

            while (samplesProcessed < totalSamples)
            {
                short[] frameSamples = new short[28];
                int samplesToCopy = Math.Min(28, totalSamples - samplesProcessed);
                Array.Copy(samples, samplesProcessed, frameSamples, 0, samplesToCopy);

                if (samplesToCopy < 28)
                {
                    for (int i = samplesToCopy; i < 28; i++)
                        frameSamples[i] = 0;
                }

                byte[] frameData = CompressFrame(frameSamples, ref lpcTap);

                if (loopStart != -1 && samplesProcessed == loopStart)
                    frameData[1] |= 0x04;
                if (loopEnd != -1 && samplesProcessed + 28 == loopEnd)
                    frameData[1] |= 0x03;

                writer.Write(frameData);

                samplesProcessed += 28;

                if (samplesProcessed >= totalSamples && !EnableLooping)
                    flags = 1;

                if (EnableLooping)
                    flags = 2;
            }

            writer.Write(new byte[16]);
            ms.Seek(-16, SeekOrigin.Current);
            writer.Write((byte)(EnableLooping ? 3 : 7));

            return ms.ToArray();
        }

        private byte[] CompressFrame(short[] samples, ref int[] lpcTap)
        {
            int[,] lpc = new int[,]
            {
                { 0, 0 },
                { 60, 0 },
                { 115, -52 },
                { 98, -55 },
                { 122, -60 }
            };

            ulong bestError = ulong.MaxValue;
            byte[] bestFrame = new byte[16];
            int[] bestTap = new int[2];

            for (int filter = 0; filter < 5; filter++)
            {
                for (int shift = 0; shift <= 12; shift++)
                {
                    int[] tap = new int[] { lpcTap[0], lpcTap[1] };
                    ulong error = 0;
                    byte[] testFrame = new byte[16];

                    testFrame[0] = (byte)((12 - shift) | (filter << 4));
                    testFrame[1] = 0;
                    testFrame[2] = 0;
                    testFrame[3] = 0;
                    testFrame[4] = 0;
                    testFrame[5] = 0;
                    testFrame[6] = 0;
                    testFrame[7] = 0;
                    testFrame[8] = 0;
                    testFrame[9] = 0;
                    testFrame[10] = 0;
                    testFrame[11] = 0;
                    testFrame[12] = 0;
                    testFrame[13] = 0;
                    testFrame[14] = 0;
                    testFrame[15] = 0;

                    for (int n = 0; n < 28; n++)
                    {
                        int x = samples[n];

                        int p = (tap[0] * lpc[filter, 0] + tap[1] * lpc[filter, 1] + 32) >> 6;

                        int r = x - p;

                        int rounding = (1 << shift) - ((r < 0) ? 1 : 0);
                        int q = (r + (rounding >> 1)) >> shift;

                        if (q < -8) q = -8;
                        if (q > 7) q = 7;

                        int y = p + (q << shift);
                        if (y < -32768) y = -32768;
                        if (y > 32767) y = 32767;

                        int e = y - x;

                        int byteIndex = (n + 4) / 2;
                        int nibbleShift = ((n + 4) % 2) * 4;
                        testFrame[byteIndex] |= (byte)((q & 0xF) << nibbleShift);

                        error += (ulong)(e * e);

                        tap[1] = tap[0];
                        tap[0] = y;
                    }

                    if (error < bestError)
                    {
                        bestError = error;
                        bestFrame = testFrame;
                        bestTap[0] = tap[0];
                        bestTap[1] = tap[1];
                    }
                }
            }

            lpcTap[0] = bestTap[0];
            lpcTap[1] = bestTap[1];
            return bestFrame;
        }

        private void WriteUint32BE(BinaryWriter writer, uint value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private short[] ResampleSamples(short[] samples, double factor, int type)
        {
            if (Math.Abs(factor - 1.0) < 0.001) return samples;

            int newLength = (int)(samples.Length * factor);
            short[] result = new short[newLength];
            double step = 1.0 / factor;
            double position = 0;

            for (int i = 0; i < newLength; i++)
            {
                int index = (int)position;
                double frac = position - index;

                if (index >= samples.Length - 1)
                {
                    result[i] = samples[samples.Length - 1];
                }
                else if (type == 0)
                {
                    int x0 = samples[Math.Max(0, index)];
                    int x1 = samples[Math.Min(samples.Length - 1, index + 1)];
                    result[i] = (short)Math.Round(x0 + (x1 - x0) * frac);
                }
                else if (type == 1)
                {
                    int xm1 = samples[Math.Max(0, index - 1)];
                    int x0 = samples[index];
                    int x1 = samples[Math.Min(samples.Length - 1, index + 1)];
                    int x2 = samples[Math.Min(samples.Length - 1, index + 2)];

                    double a0 = 2 * x0;
                    double a1 = -xm1 + x1;
                    double a2 = 2 * xm1 - 5 * x0 + 4 * x1 - x2;
                    double a3 = -xm1 + 3 * x0 - 3 * x1 + x2;

                    double v = (a0 + frac * (a1 + frac * (a2 + frac * a3))) * 0.5;
                    int vi = (int)Math.Round(v);
                    if (vi < -32768) vi = -32768;
                    if (vi > 32767) vi = 32767;
                    result[i] = (short)vi;
                }
                else
                {
                    int window = 16;
                    double sum = 0;
                    double weightSum = 0;

                    for (int j = -window; j <= window; j++)
                    {
                        int sampleIndex = index + j;
                        if (sampleIndex < 0) sampleIndex = 0;
                        if (sampleIndex >= samples.Length) sampleIndex = samples.Length - 1;

                        double weight;
                        if (Math.Abs(j - frac) < 0.001)
                        {
                            weight = 1;
                        }
                        else
                        {
                            weight = Math.Sin(Math.PI * (j - frac)) / (Math.PI * (j - frac));
                        }

                        weight *= 0.54 - 0.46 * Math.Cos(2 * Math.PI * (j + window) / (2 * window));
                        sum += samples[sampleIndex] * weight;
                        weightSum += weight;
                    }

                    int vi2 = (int)Math.Round(sum / weightSum);
                    if (vi2 < -32768) vi2 = -32768;
                    if (vi2 > 32767) vi2 = 32767;
                    result[i] = (short)vi2;
                }

                position += step;
            }

            return result;
        }

        private (int loopStart, int loopEnd) ParseLoopPoints(string fileName)
        {
            int loopStart = -1;
            int loopEnd = -1;

            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"_(\d+)_(\d+)");
            if (match.Success)
            {
                int.TryParse(match.Groups[1].Value, out loopStart);
                int.TryParse(match.Groups[2].Value, out loopEnd);
            }

            return (loopStart, loopEnd);
        }
    }
}