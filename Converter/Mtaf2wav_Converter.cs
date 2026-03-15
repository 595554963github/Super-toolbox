using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Mtaf2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private const int FRAME_SIZE = 0x110;
        private const int FRAME_SAMPLES = 256;
        private const int HEADER_SIZE = 0x800;
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
                    var mtafFiles = Directory.GetFiles(directoryPath, "*.mtaf", SearchOption.AllDirectories)
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

                    TotalFilesToConvert = mtafFiles.Length;
                    int successCount = 0;

                    if (mtafFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的MTAF文件");
                        OnConversionFailed("未找到需要转换的MTAF文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个MTAF文件");

                    foreach (var mtafFilePath in mtafFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(mtafFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.mtaf");

                        string fileDirectory = Path.GetDirectoryName(mtafFilePath) ?? string.Empty;
                        string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                        try
                        {
                            if (ConvertMtafToWav(mtafFilePath, wavFilePath) && File.Exists(wavFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.wav");
                                OnFileConverted(wavFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.mtaf转换失败");
                                OnConversionFailed($"{fileName}.mtaf转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.mtaf处理错误:{ex.Message}");
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

        private (List<short> samples, int hist, int step) DecodeFrameChannel(byte[] frame, int ch, int hist, int step)
        {
            var samples = new List<short>();
            byte[] nibbleData = new byte[0x80];
            Array.Copy(frame, 0x10 + 0x80 * ch, nibbleData, 0, 0x80);

            for (int i = 0; i < FRAME_SAMPLES; i++)
            {
                byte nibbles = nibbleData[i / 2];
                int nibble;
                if ((i & 1) != 0)
                    nibble = (nibbles >> 4) & 0xF;
                else
                    nibble = nibbles & 0xF;

                hist = Clamp16(hist + STEP_SIZES[step][nibble]);
                samples.Add((short)hist);

                step += STEP_INDEXES[nibble];
                if (step < 0) step = 0;
                else if (step > 31) step = 31;
            }

            return (samples, hist, step);
        }

        private bool ConvertMtafToWav(string mtafFilePath, string wavFilePath)
        {
            try
            {
                using (var fs = new FileStream(mtafFilePath, FileMode.Open))
                using (var br = new BinaryReader(fs))
                {
                    byte[] header = br.ReadBytes(HEADER_SIZE);

                    if (header[0] != HEADER_NAME[0] || header[1] != HEADER_NAME[1] ||
                        header[2] != HEADER_NAME[2] || header[3] != HEADER_NAME[3])
                    {
                        ConversionError?.Invoke(this, "不是有效的MTAF文件");
                        return false;
                    }

                    uint totalSamples = BitConverter.ToUInt32(header, 0x5C);
                    byte tracks = header[0x61];
                    if (tracks <= 0) tracks = 1;

                    int channels = tracks * 2;
                    int frames = (int)((totalSamples + FRAME_SAMPLES - 1) / FRAME_SAMPLES);

                    var outputs = new List<short>[channels];
                    for (int i = 0; i < channels; i++)
                        outputs[i] = new List<short>();

                    int[] hists = new int[channels];
                    int[] steps = new int[channels];

                    for (int frameIndex = 0; frameIndex < frames; frameIndex++)
                    {
                        for (int t = 0; t < tracks; t++)
                        {
                            if (fs.Position + FRAME_SIZE > fs.Length)
                                break;

                            byte[] frame = br.ReadBytes(FRAME_SIZE);
                            if (frame.Length < FRAME_SIZE)
                                break;

                            int chL = t * 2;
                            int chR = chL + 1;

                            steps[chL] = BitConverter.ToInt16(frame, 0x04);
                            steps[chR] = BitConverter.ToInt16(frame, 0x06);
                            hists[chL] = BitConverter.ToInt16(frame, 0x08);
                            hists[chR] = BitConverter.ToInt16(frame, 0x0C);

                            if (steps[chL] < 0) steps[chL] = 0;
                            else if (steps[chL] > 31) steps[chL] = 31;
                            if (steps[chR] < 0) steps[chR] = 0;
                            else if (steps[chR] > 31) steps[chR] = 31;

                            var resultL = DecodeFrameChannel(frame, 0, hists[chL], steps[chL]);
                            var resultR = DecodeFrameChannel(frame, 1, hists[chR], steps[chR]);

                            hists[chL] = resultL.hist;
                            steps[chL] = resultL.step;
                            hists[chR] = resultR.hist;
                            steps[chR] = resultR.step;

                            outputs[chL].AddRange(resultL.samples);
                            outputs[chR].AddRange(resultR.samples);
                        }
                    }

                    for (int ch = 0; ch < channels; ch++)
                    {
                        if (outputs[ch].Count > (int)totalSamples)
                            outputs[ch] = outputs[ch].Take((int)totalSamples).ToList();
                    }

                    var interleaved = new List<short>();
                    for (int i = 0; i < totalSamples; i++)
                    {
                        for (int ch = 0; ch < channels; ch++)
                        {
                            interleaved.Add(outputs[ch][i]);
                        }
                    }

                    using (var wavStream = new FileStream(wavFilePath, FileMode.Create))
                    using (var writer = new BinaryWriter(wavStream))
                    {
                        int byteRate = 48000 * channels * 2;
                        int dataSize = interleaved.Count * 2;

                        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                        writer.Write(dataSize + 36);
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                        writer.Write(16);
                        writer.Write((short)1);
                        writer.Write((short)channels);
                        writer.Write(48000);
                        writer.Write(byteRate);
                        writer.Write((short)(channels * 2));
                        writer.Write((short)16);
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                        writer.Write(dataSize);

                        foreach (short sample in interleaved)
                        {
                            writer.Write(sample);
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

        private int Clamp16(int x)
        {
            if (x > 32767) return 32767;
            if (x < -32768) return -32768;
            return x;
        }
    }
}