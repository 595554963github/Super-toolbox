using System.Text.RegularExpressions;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Formats.Pcm16;

namespace super_toolbox
{
    public class Wav2mtaf_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public int ResamplingType { get; set; } = 2;

        private const int FRAME_SIZE = 0x110;
        private const int FRAME_SAMPLES = 256;
        private const int HEADER_SIZE = 0x800;
        private const int TARGET_SAMPLE_RATE = 48000;
        private static readonly byte[] HEADER_NAME = new byte[] { (byte)'M', (byte)'T', (byte)'A', (byte)'F' };

        private static readonly int[] STEP_INDEXES = new int[]
        {
            -1,-1,-1,-1,2,4,6,8,
            -1,-1,-1,-1,2,4,6,8
        };

        private static readonly int[][] STEP_SIZES = new int[][]
        {
            new int[] {1,5,9,13,16,20,24,28,-1,-5,-9,-13,-16,-20,-24,-28},
            new int[] {2,6,11,15,20,24,29,33,-2,-6,-11,-15,-20,-24,-29,-33},
            new int[] {2,7,13,18,23,28,34,39,-2,-7,-13,-18,-23,-28,-34,-39},
            new int[] {3,9,15,21,28,34,40,46,-3,-9,-15,-21,-28,-34,-40,-46},
            new int[] {3,11,18,26,33,41,48,56,-3,-11,-18,-26,-33,-41,-48,-56},
            new int[] {4,13,22,31,40,49,58,67,-4,-13,-22,-31,-40,-49,-58,-67},
            new int[] {5,16,26,37,48,59,69,80,-5,-16,-26,-37,-48,-59,-69,-80},
            new int[] {6,19,31,44,57,70,82,95,-6,-19,-31,-44,-57,-70,-82,-95},
            new int[] {7,22,38,53,68,83,99,114,-7,-22,-38,-53,-68,-83,-99,-114},
            new int[] {9,27,45,63,81,99,117,135,-9,-27,-45,-63,-81,-99,-117,-135},
            new int[] {10,32,53,75,96,118,139,161,-10,-32,-53,-75,-96,-118,-139,-161},
            new int[] {12,38,64,90,115,141,167,193,-12,-38,-64,-90,-115,-141,-167,-193},
            new int[] {15,45,76,106,137,167,198,228,-15,-45,-76,-106,-137,-167,-198,-228},
            new int[] {18,54,91,127,164,200,237,273,-18,-54,-91,-127,-164,-200,-237,-273},
            new int[] {21,65,108,152,195,239,282,326,-21,-65,-108,-152,-195,-239,-282,-326},
            new int[] {25,77,129,181,232,284,336,388,-25,-77,-129,-181,-232,-284,-336,-388},
            new int[] {30,92,153,215,276,338,399,461,-30,-92,-153,-215,-276,-338,-399,-461},
            new int[] {36,109,183,256,329,402,476,549,-36,-109,-183,-256,-329,-402,-476,-549},
            new int[] {43,130,218,305,392,479,567,654,-43,-130,-218,-305,-392,-479,-567,-654},
            new int[] {52,156,260,364,468,572,676,780,-52,-156,-260,-364,-468,-572,-676,-780},
            new int[] {62,186,310,434,558,682,806,930,-62,-186,-310,-434,-558,-682,-806,-930},
            new int[] {73,221,368,516,663,811,958,1106,-73,-221,-368,-516,-663,-811,-958,-1106},
            new int[] {87,263,439,615,790,966,1142,1318,-87,-263,-439,-615,-790,-966,-1142,-1318},
            new int[] {104,314,523,733,942,1152,1361,1571,-104,-314,-523,-733,-942,-1152,-1361,-1571},
            new int[] {124,374,623,873,1122,1372,1621,1871,-124,-374,-623,-873,-1122,-1372,-1621,-1871},
            new int[] {148,445,743,1040,1337,1634,1932,2229,-148,-445,-743,-1040,-1337,-1634,-1932,-2229},
            new int[] {177,531,885,1239,1593,1947,2301,2655,-177,-531,-885,-1239,-1593,-1947,-2301,-2655},
            new int[] {210,632,1053,1475,1896,2318,2739,3161,-210,-632,-1053,-1475,-1896,-2318,-2739,-3161},
            new int[] {251,753,1255,1757,2260,2762,3264,3766,-251,-753,-1255,-1757,-2260,-2762,-3264,-3766},
            new int[] {299,897,1495,2093,2692,3290,3888,4486,-299,-897,-1495,-2093,-2692,-3290,-3888,-4486},
            new int[] {356,1068,1781,2493,3206,3918,4631,5343,-356,-1068,-1781,-2493,-3206,-3918,-4631,-5343},
            new int[] {424,1273,2121,2970,3819,4668,5516,6365,-424,-1273,-2121,-2970,-3819,-4668,-5516,-6365}
        };

        private static int[][] ComputeNextStepTable()
        {
            int[][] nextStep = new int[32][];
            for (int s = 0; s < 32; s++)
            {
                nextStep[s] = new int[16];
                for (int n = 0; n < 16; n++)
                {
                    int ns = s + STEP_INDEXES[n];
                    if (ns < 0) ns = 0;
                    else if (ns > 31) ns = 31;
                    nextStep[s][n] = ns;
                }
            }
            return nextStep;
        }

        private static readonly int[][] NEXT_STEP = ComputeNextStepTable();

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

                    if (wavFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的WAV文件");
                        OnConversionFailed("未找到需要转换的WAV文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个WAV文件");

                    foreach (var wavFilePath in wavFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.wav");

                        string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                        string mtafFilePath = Path.Combine(fileDirectory, $"{fileName}.mtaf");

                        try
                        {
                            if (ConvertWavToMtaf(wavFilePath, mtafFilePath) && File.Exists(mtafFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.mtaf");
                                OnFileConverted(mtafFilePath);
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

                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }, cancellationToken);
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

        private bool ConvertWavToMtaf(string wavFilePath, string mtafFilePath)
        {
            try
            {
                var wavReader = new WaveReader();
                AudioData audioData;

                using (var wavStream = File.OpenRead(wavFilePath))
                {
                    audioData = wavReader.Read(wavStream);
                }

                if (audioData == null)
                    return false;

                var pcmFormat = audioData.GetFormat<Pcm16Format>();
                int sampleRate = pcmFormat.SampleRate;
                short[][] channels = pcmFormat.Channels;

                if (channels.Length != 2)
                {
                    ConversionError?.Invoke(this, "必须是立体声");
                    return false;
                }

                double resampleFactor = 1.0;
                if (sampleRate != TARGET_SAMPLE_RATE)
                {
                    resampleFactor = (double)TARGET_SAMPLE_RATE / sampleRate;
                    ConversionProgress?.Invoke(this, $"采样率{sampleRate}Hz不符合要求,自动重采样至{TARGET_SAMPLE_RATE}Hz");
                }

                short[] left = channels[0];
                short[] right = channels[1];

                if (Math.Abs(resampleFactor - 1.0) > 0.001)
                {
                    left = ResampleSamples(left, resampleFactor, ResamplingType);
                    right = ResampleSamples(right, resampleFactor, ResamplingType);
                }

                int totalSamples = left.Length;
                int frames = (totalSamples + FRAME_SAMPLES - 1) / FRAME_SAMPLES;

                using (var fs = new FileStream(mtafFilePath, FileMode.Create))
                using (var bw = new BinaryWriter(fs))
                {
                    byte[] header = new byte[HEADER_SIZE];
                    Array.Copy(HEADER_NAME, 0, header, 0, 4);

                    byte[] headMarker = BitConverter.GetBytes(0x44414548);
                    Array.Copy(headMarker, 0, header, 0x40, 4);

                    byte[] sampleCount = BitConverter.GetBytes((uint)totalSamples);
                    Array.Copy(sampleCount, 0, header, 0x5C, 4);

                    header[0x61] = 1;

                    bw.Write(header);

                    int histL = 0, histR = 0;
                    int stepL = 0, stepR = 0;
                    int pos = 0;

                    for (int frameIndex = 0; frameIndex < frames; frameIndex++)
                    {
                        int samplesToTake = Math.Min(FRAME_SAMPLES, totalSamples - pos);

                        short[] l = new short[FRAME_SAMPLES];
                        short[] r = new short[FRAME_SAMPLES];

                        Array.Copy(left, pos, l, 0, samplesToTake);
                        Array.Copy(right, pos, r, 0, samplesToTake);

                        pos += FRAME_SAMPLES;

                        byte[] framebuf = new byte[FRAME_SIZE];

                        byte[] stepLBuf = BitConverter.GetBytes((short)stepL);
                        byte[] stepRBuf = BitConverter.GetBytes((short)stepR);
                        Array.Copy(stepLBuf, 0, framebuf, 4, 2);
                        Array.Copy(stepRBuf, 0, framebuf, 6, 2);

                        byte[] histLBuf = BitConverter.GetBytes((short)histL);
                        byte[] histRBuf = BitConverter.GetBytes((short)histR);
                        Array.Copy(histLBuf, 0, framebuf, 8, 2);
                        Array.Copy(histRBuf, 0, framebuf, 12, 2);

                        var ln = EncodeChannelFrame(l, ref histL, ref stepL);
                        var rn = EncodeChannelFrame(r, ref histR, ref stepR);

                        byte[] leftNibbles = PackNibbles(ln);
                        byte[] rightNibbles = PackNibbles(rn);

                        Array.Copy(leftNibbles, 0, framebuf, 0x10, leftNibbles.Length);
                        Array.Copy(rightNibbles, 0, framebuf, 0x90, rightNibbles.Length);

                        bw.Write(framebuf);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private int[] EncodeChannelFrame(short[] samples, ref int hist, ref int step)
        {
            int[] nibbles = new int[FRAME_SAMPLES];

            for (int i = 0; i < FRAME_SAMPLES; i++)
            {
                int sample = samples[i];
                int[] sizes = STEP_SIZES[step];

                int bestN = 0;
                int bestErr = int.MaxValue;
                int bestHist = hist;

                int start = sample >= hist ? 0 : 8;
                int end = start + 8;

                for (int n = start; n < end; n++)
                {
                    int pred = Clamp16(hist + sizes[n]);
                    int err = Math.Abs(sample - pred);

                    if (err < bestErr)
                    {
                        bestErr = err;
                        bestN = n;
                        bestHist = pred;
                    }
                }

                hist = bestHist;
                step = NEXT_STEP[step][bestN];
                nibbles[i] = bestN;
            }

            return nibbles;
        }

        private byte[] PackNibbles(int[] nibbles)
        {
            byte[] result = new byte[nibbles.Length / 2];
            int j = 0;
            for (int i = 0; i < nibbles.Length; i += 2)
            {
                result[j++] = (byte)(nibbles[i] | (nibbles[i + 1] << 4));
            }
            return result;
        }

        private short Clamp16(int x)
        {
            if (x > 32767) return 32767;
            if (x < -32768) return -32768;
            return (short)x;
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
    }
}