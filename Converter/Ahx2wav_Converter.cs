using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Ahx2wav_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private static readonly int[] BitAllocTable = { 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 };
        private static readonly int[][] OffsetTable = {
            new int[] { 0 },
            new int[] { 0 },
            new int[] { 0, 1,  3, 4 },
            new int[] { 0, 1,  3, 4, 5, 6,  7, 8 },
            new int[] { 0, 1,  2, 3, 4, 5,  6, 7,  8,  9, 10, 11, 12, 13, 14 }
        };

        private static readonly int[] QcNLevels = { 3, 5, 7, 9, 15, 31, 63, 127, 255, 511, 1023, 2047, 4095, 8191, 16383, 32767, 65535 };
        private static readonly int[] QcBits = { -5, -7, 3, -10, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        private static readonly int[] IntWinBase = {
            0,    -1,    -1,    -1,    -1,    -1,    -1,    -2,    -2,    -2,    -2,    -3,    -3,    -4,    -4,    -5,
            -5,    -6,    -7,    -7,    -8,    -9,   -10,   -11,   -13,   -14,   -16,   -17,   -19,   -21,   -24,   -26,
            -29,   -31,   -35,   -38,   -41,   -45,   -49,   -53,   -58,   -63,   -68,   -73,   -79,   -85,   -91,   -97,
            -104,  -111,  -117,  -125,  -132,  -139,  -147,  -154,  -161,  -169,  -176,  -183,  -190,  -196,  -202,  -208,
            -213,  -218,  -222,  -225,  -227,  -228,  -228,  -227,  -224,  -221,  -215,  -208,  -200,  -189,  -177,  -163,
            -146,  -127,  -106,   -83,   -57,   -29,     2,    36,    72,   111,   153,   197,   244,   294,   347,   401,
            459,   519,   581,   645,   711,   779,   848,   919,   991,  1064,  1137,  1210,  1283,  1356,  1428,  1498,
            1567,  1634,  1698,  1759,  1817,  1870,  1919,  1962,  2001,  2032,  2057,  2075,  2085,  2087,  2080,  2063,
            2037,  2000,  1952,  1893,  1822,  1739,  1644,  1535,  1414,  1280,  1131,   970,   794,   605,   402,   185,
            -45,  -288,  -545,  -814, -1095, -1388, -1692, -2006, -2330, -2663, -3004, -3351, -3705, -4063, -4425, -4788,
            -5153, -5517, -5879, -6237, -6589, -6935, -7271, -7597, -7910, -8209, -8491, -8755, -8998, -9219, -9416, -9585,
            -9727, -9838, -9916, -9959, -9966, -9935, -9863, -9750, -9592, -9389, -9139, -8840, -8492, -8092, -7640, -7134,
            -6574, -5959, -5288, -4561, -3776, -2935, -2037, -1082,   -70,   998,  2122,  3300,  4533,  5818,  7154,  8540,
            9975, 11455, 12980, 14548, 16155, 17799, 19478, 21189, 22929, 24694, 26482, 28289, 30112, 31947, 33791, 35640,
            37489, 39336, 41176, 43006, 44821, 46617, 48390, 50137, 51853, 53534, 55178, 56778, 58333, 59838, 61289, 62684,
            64019, 65290, 66494, 67629, 68692, 69679, 70590, 71420, 72169, 72835, 73415, 73908, 74313, 74630, 74856, 74992,
            75038
        };

        private double[] CosTable0 = new double[16];
        private double[] CosTable1 = new double[8];
        private double[] CosTable2 = new double[4];
        private double[] CosTable3 = new double[2];
        private double[] CosTable4 = new double[1];
        private double[] PowTable = new double[64];
        private double[] DecWin = new double[544];

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            InitializeTables();

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var ahxFiles = Directory.GetFiles(directoryPath, "*.ahx", SearchOption.AllDirectories)
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

            TotalFilesToConvert = ahxFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var ahxFilePath in ahxFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(ahxFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.ahx");

                    string fileDirectory = Path.GetDirectoryName(ahxFilePath) ?? string.Empty;

                    try
                    {
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertAhxToWav(ahxFilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.ahx转换失败");
                            OnConversionFailed($"{fileName}.ahx转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.ahx处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                else
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");

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

        private void InitializeTables()
        {
            for (int i = 0; i < 16; i++) CosTable0[i] = 0.5 / Math.Cos(Math.PI * ((i << 1) + 1) / 64.0);
            for (int i = 0; i < 8; i++) CosTable1[i] = 0.5 / Math.Cos(Math.PI * ((i << 1) + 1) / 32.0);
            for (int i = 0; i < 4; i++) CosTable2[i] = 0.5 / Math.Cos(Math.PI * ((i << 1) + 1) / 16.0);
            for (int i = 0; i < 2; i++) CosTable3[i] = 0.5 / Math.Cos(Math.PI * ((i << 1) + 1) / 8.0);
            for (int i = 0; i < 1; i++) CosTable4[i] = 0.5 / Math.Cos(Math.PI * ((i << 1) + 1) / 4.0);
            for (int i = 0; i < 64; i++) PowTable[i] = Math.Pow(2.0, (3 - i) / 3.0);

            int j = 0;
            for (int i = 0; i < 256; i++, j += 32)
            {
                if (j < 528)
                {
                    double val = IntWinBase[i] / 65536.0 * 32768.0 * ((i & 64) != 0 ? +1.0 : -1.0);
                    DecWin[j] = val;
                    DecWin[j + 16] = val;
                }
                if ((i & 31) == 31) j -= 1023;
            }
            for (int i = 0, j2 = 8; i < 256; i++, j2 += 32)
            {
                if (j2 < 528)
                {
                    double val = IntWinBase[256 - i] / 65536.0 * 32768.0 * ((i & 64) != 0 ? +1.0 : -1.0);
                    DecWin[j2] = val;
                    DecWin[j2 + 16] = val;
                }
                if ((i & 31) == 31) j2 -= 1023;
            }
        }

        private bool ConvertAhxToWav(string ahxFilePath, string wavFile, CancellationToken cancellationToken)
        {
            try
            {
                byte[] ahxBuf = File.ReadAllBytes(ahxFilePath);

                uint frequency = GetLongBigEndian(ahxBuf, 8);
                uint sampleCount = GetLongBigEndian(ahxBuf, 12);
                int wavBufLen = (int)sampleCount * 2;

                if (wavBufLen > int.MaxValue - 1152 * 16)
                {
                    ConversionError?.Invoke(this, "WAV缓冲区大小超出限制");
                    return false;
                }

                byte[] wavBuf = new byte[wavBufLen + 1152 * 16];
                int decodedSize = DecodeAhx(ahxBuf, wavBuf, ahxBuf.Length);

                WriteWavFile(wavFile, wavBuf, decodedSize, (int)frequency);

                return decodedSize > 1 && File.Exists(wavFile);
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                return false;
            }
        }

        private uint GetLongBigEndian(byte[] buf, int offset)
        {
            return (uint)(buf[offset] << 24 | buf[offset + 1] << 16 | buf[offset + 2] << 8 | buf[offset + 3]);
        }

        private int GetBits(byte[] src, ref int srcOffset, ref ulong bitData, ref int bitRest, int bits)
        {
            while (bitRest < 24 && srcOffset < src.Length)
            {
                bitData <<= 8;
                bitData |= src[srcOffset++];
                bitRest += 8;
            }
            int ret = (int)((bitData >> (bitRest - bits)) & ((1u << bits) - 1));
            bitRest -= bits;
            return ret;
        }

        private int DecodeAhx(byte[] src, byte[] dst, int srcLen)
        {
            int sampleIndex = 0;
            int bitRest = 0;
            ulong bitData = 0;
            int srcOffset = src[2] * 256 + src[3] + 4;
            int srcStartOffset = srcOffset;

            int[] bitAlloc = new int[32];
            int[] scfsi = new int[32];
            int[,] scalefactor = new int[32, 3];
            double[,] sbsamples = new double[36, 32];
            double[,,] dctbuf = new double[2, 16, 17];
            int phase = 0;

            while (srcOffset - srcStartOffset < srcLen && GetBits(src, ref srcOffset, ref bitData, ref bitRest, 12) == 0xfff)
            {
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 1);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 2);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 1);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 4);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 2);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 1);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 1);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 2);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 2);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 1);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 1);
                GetBits(src, ref srcOffset, ref bitData, ref bitRest, 2);

                for (int sb = 0; sb < 30; sb++)
                    bitAlloc[sb] = GetBits(src, ref srcOffset, ref bitData, ref bitRest, BitAllocTable[sb]);

                for (int sb = 0; sb < 30; sb++)
                    if (bitAlloc[sb] != 0)
                        scfsi[sb] = GetBits(src, ref srcOffset, ref bitData, ref bitRest, 2);

                for (int sb = 0; sb < 30; sb++)
                {
                    if (bitAlloc[sb] != 0)
                    {
                        scalefactor[sb, 0] = GetBits(src, ref srcOffset, ref bitData, ref bitRest, 6);
                        switch (scfsi[sb])
                        {
                            case 0:
                                scalefactor[sb, 1] = GetBits(src, ref srcOffset, ref bitData, ref bitRest, 6);
                                scalefactor[sb, 2] = GetBits(src, ref srcOffset, ref bitData, ref bitRest, 6);
                                break;
                            case 1:
                                scalefactor[sb, 1] = scalefactor[sb, 0];
                                scalefactor[sb, 2] = GetBits(src, ref srcOffset, ref bitData, ref bitRest, 6);
                                break;
                            case 2:
                                scalefactor[sb, 1] = scalefactor[sb, 0];
                                scalefactor[sb, 2] = scalefactor[sb, 0];
                                break;
                            case 3:
                                scalefactor[sb, 1] = scalefactor[sb, 2] = GetBits(src, ref srcOffset, ref bitData, ref bitRest, 6);
                                break;
                        }
                    }
                }

                for (int gr = 0; gr < 12; gr++)
                {
                    for (int sb = 0; sb < 30; sb++)
                    {
                        if (bitAlloc[sb] != 0)
                        {
                            int idx = OffsetTable[BitAllocTable[sb]][bitAlloc[sb] - 1];
                            int nl = QcNLevels[idx];
                            int qb = QcBits[idx];
                            if (qb < 0)
                            {
                                int t = GetBits(src, ref srcOffset, ref bitData, ref bitRest, -qb);
                                int q = (t % nl) * 2 - nl + 1;
                                sbsamples[gr * 3, sb] = (double)q / nl;
                                t /= nl;
                                q = (t % nl) * 2 - nl + 1;
                                sbsamples[gr * 3 + 1, sb] = (double)q / nl;
                                t /= nl;
                                q = t * 2 - nl + 1;
                                sbsamples[gr * 3 + 2, sb] = (double)q / nl;
                            }
                            else
                            {
                                int q = GetBits(src, ref srcOffset, ref bitData, ref bitRest, qb) * 2 - nl + 1;
                                sbsamples[gr * 3, sb] = (double)q / nl;
                                q = GetBits(src, ref srcOffset, ref bitData, ref bitRest, qb) * 2 - nl + 1;
                                sbsamples[gr * 3 + 1, sb] = (double)q / nl;
                                q = GetBits(src, ref srcOffset, ref bitData, ref bitRest, qb) * 2 - nl + 1;
                                sbsamples[gr * 3 + 2, sb] = (double)q / nl;
                            }
                        }
                        else
                        {
                            sbsamples[gr * 3, sb] = 0;
                            sbsamples[gr * 3 + 1, sb] = 0;
                            sbsamples[gr * 3 + 2, sb] = 0;
                        }
                        double pt = PowTable[scalefactor[sb, gr >> 2]];
                        sbsamples[gr * 3, sb] *= pt;
                        sbsamples[gr * 3 + 1, sb] *= pt;
                        sbsamples[gr * 3 + 2, sb] *= pt;
                    }
                }

                for (int gr = 0; gr < 36; gr++)
                {
                    if ((phase & 1) != 0)
                        Dct(sbsamples, gr, dctbuf, 0, (phase + 1) & 15, 1, phase);
                    else
                        Dct(sbsamples, gr, dctbuf, 1, phase, 0, (phase + 1) & 15);

                    int win = 16 - (phase | 1);

                    for (int i = 0; i < 16; i++, win += 16)
                    {
                        double sum = DecWin[win++] * dctbuf[phase & 1, 0, i];
                        sum -= DecWin[win++] * dctbuf[phase & 1, 1, i];
                        sum += DecWin[win++] * dctbuf[phase & 1, 2, i];
                        sum -= DecWin[win++] * dctbuf[phase & 1, 3, i];
                        sum += DecWin[win++] * dctbuf[phase & 1, 4, i];
                        sum -= DecWin[win++] * dctbuf[phase & 1, 5, i];
                        sum += DecWin[win++] * dctbuf[phase & 1, 6, i];
                        sum -= DecWin[win++] * dctbuf[phase & 1, 7, i];
                        sum += DecWin[win++] * dctbuf[phase & 1, 8, i];
                        sum -= DecWin[win++] * dctbuf[phase & 1, 9, i];
                        sum += DecWin[win++] * dctbuf[phase & 1, 10, i];
                        sum -= DecWin[win++] * dctbuf[phase & 1, 11, i];
                        sum += DecWin[win++] * dctbuf[phase & 1, 12, i];
                        sum -= DecWin[win++] * dctbuf[phase & 1, 13, i];
                        sum += DecWin[win++] * dctbuf[phase & 1, 14, i];
                        sum -= DecWin[win++] * dctbuf[phase & 1, 15, i];
                        WriteSample(dst, ref sampleIndex, ref sum);
                    }

                    {
                        double sum = DecWin[win] * dctbuf[phase & 1, 0, 16];
                        sum += DecWin[win + 2] * dctbuf[phase & 1, 2, 16];
                        sum += DecWin[win + 4] * dctbuf[phase & 1, 4, 16];
                        sum += DecWin[win + 6] * dctbuf[phase & 1, 6, 16];
                        sum += DecWin[win + 8] * dctbuf[phase & 1, 8, 16];
                        sum += DecWin[win + 10] * dctbuf[phase & 1, 10, 16];
                        sum += DecWin[win + 12] * dctbuf[phase & 1, 12, 16];
                        sum += DecWin[win + 14] * dctbuf[phase & 1, 14, 16];
                        WriteSample(dst, ref sampleIndex, ref sum);
                    }

                    win += -16 + (phase | 1) * 2;

                    for (int i = 15; i >= 1; i--)
                    {
                        double sum = -DecWin[--win] * dctbuf[phase & 1, 0, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 1, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 2, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 3, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 4, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 5, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 6, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 7, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 8, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 9, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 10, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 11, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 12, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 13, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 14, i];
                        sum -= DecWin[--win] * dctbuf[phase & 1, 15, i];
                        WriteSample(dst, ref sampleIndex, ref sum);
                    }

                    phase = (phase - 1) & 15;
                }

                if ((bitRest & 7) != 0) GetBits(src, ref srcOffset, ref bitData, ref bitRest, bitRest & 7);
            }

            return sampleIndex;
        }

        private void Dct(double[,] sbs, int gr, double[,,] dctbuf, int bufIdx0, int phaseIdx0, int bufIdx1, int phaseIdx1)
        {
            double[] t0 = new double[32];
            double[] t1 = new double[32];

            for (int i = 0; i < 32; i++)
                t0[i] = (i & 16) != 0
                    ? (-sbs[gr, i] + sbs[gr, 31 ^ i]) * CosTable0[~i & 15]
                    : (+sbs[gr, i] + sbs[gr, 31 ^ i]);

            for (int i = 0; i < 32; i++)
                t1[i] = (i & 8) != 0
                    ? (-t0[i] + t0[15 ^ i]) * CosTable1[~i & 7] * ((i & 16) != 0 ? -1.0 : 1.0)
                    : (+t0[i] + t0[15 ^ i]);

            for (int i = 0; i < 32; i++)
                t0[i] = (i & 4) != 0
                    ? (-t1[i] + t1[7 ^ i]) * CosTable2[~i & 3] * ((i & 8) != 0 ? -1.0 : 1.0)
                    : (+t1[i] + t1[7 ^ i]);

            for (int i = 0; i < 32; i++)
                t1[i] = (i & 2) != 0
                    ? (-t0[i] + t0[3 ^ i]) * CosTable3[~i & 1] * ((i & 4) != 0 ? -1.0 : 1.0)
                    : (+t0[i] + t0[3 ^ i]);

            for (int i = 0; i < 32; i++)
                t0[i] = (i & 1) != 0
                    ? (-t1[i] + t1[1 ^ i]) * CosTable4[0] * ((i & 2) != 0 ? -1.0 : 1.0)
                    : (+t1[i] + t1[1 ^ i]);

            for (int i = 0; i < 32; i += 4) t0[i + 2] += t0[i + 3];
            for (int i = 0; i < 32; i += 8)
            {
                t0[i + 4] += t0[i + 6];
                t0[i + 6] += t0[i + 5];
                t0[i + 5] += t0[i + 7];
            }
            for (int i = 0; i < 32; i += 16)
            {
                t0[i + 8] += t0[i + 12];
                t0[i + 12] += t0[i + 10];
                t0[i + 10] += t0[i + 14];
                t0[i + 14] += t0[i + 9];
                t0[i + 9] += t0[i + 13];
                t0[i + 13] += t0[i + 11];
                t0[i + 11] += t0[i + 15];
            }

            // dst0 → dctbuf[bufIdx0, phaseIdx0]
            dctbuf[bufIdx0, phaseIdx0, 16] = t0[0];
            dctbuf[bufIdx0, phaseIdx0, 15] = t0[16] + t0[24];
            dctbuf[bufIdx0, phaseIdx0, 14] = t0[8];
            dctbuf[bufIdx0, phaseIdx0, 13] = t0[24] + t0[20];
            dctbuf[bufIdx0, phaseIdx0, 12] = t0[4];
            dctbuf[bufIdx0, phaseIdx0, 11] = t0[20] + t0[28];
            dctbuf[bufIdx0, phaseIdx0, 10] = t0[12];
            dctbuf[bufIdx0, phaseIdx0, 9] = t0[28] + t0[18];
            dctbuf[bufIdx0, phaseIdx0, 8] = t0[2];
            dctbuf[bufIdx0, phaseIdx0, 7] = t0[18] + t0[26];
            dctbuf[bufIdx0, phaseIdx0, 6] = t0[10];
            dctbuf[bufIdx0, phaseIdx0, 5] = t0[26] + t0[22];
            dctbuf[bufIdx0, phaseIdx0, 4] = t0[6];
            dctbuf[bufIdx0, phaseIdx0, 3] = t0[22] + t0[30];
            dctbuf[bufIdx0, phaseIdx0, 2] = t0[14];
            dctbuf[bufIdx0, phaseIdx0, 1] = t0[30] + t0[17];
            dctbuf[bufIdx0, phaseIdx0, 0] = t0[1];

            // dst1 → dctbuf[bufIdx1, phaseIdx1]
            dctbuf[bufIdx1, phaseIdx1, 0] = t0[1];
            dctbuf[bufIdx1, phaseIdx1, 1] = t0[17] + t0[25];
            dctbuf[bufIdx1, phaseIdx1, 2] = t0[9];
            dctbuf[bufIdx1, phaseIdx1, 3] = t0[25] + t0[21];
            dctbuf[bufIdx1, phaseIdx1, 4] = t0[5];
            dctbuf[bufIdx1, phaseIdx1, 5] = t0[21] + t0[29];
            dctbuf[bufIdx1, phaseIdx1, 6] = t0[13];
            dctbuf[bufIdx1, phaseIdx1, 7] = t0[29] + t0[19];
            dctbuf[bufIdx1, phaseIdx1, 8] = t0[3];
            dctbuf[bufIdx1, phaseIdx1, 9] = t0[19] + t0[27];
            dctbuf[bufIdx1, phaseIdx1, 10] = t0[11];
            dctbuf[bufIdx1, phaseIdx1, 11] = t0[27] + t0[23];
            dctbuf[bufIdx1, phaseIdx1, 12] = t0[7];
            dctbuf[bufIdx1, phaseIdx1, 13] = t0[23] + t0[31];
            dctbuf[bufIdx1, phaseIdx1, 14] = t0[15];
            dctbuf[bufIdx1, phaseIdx1, 15] = t0[31];
        }

        private void WriteSample(byte[] dst, ref int sampleIndex, ref double sum)
        {
            if (sampleIndex + 1 >= dst.Length) return;
            short sample;
            if (sum >= 32767) sample = 32767;
            else if (sum <= -32767) sample = -32767;
            else sample = (short)sum;
            dst[sampleIndex++] = (byte)(sample & 0xff);
            dst[sampleIndex++] = (byte)(sample >> 8);
        }

        private void WriteWavFile(string wavFile, byte[] wavBuf, int dataSize, int frequency)
        {
            using (var fs = File.Create(wavFile))
            using (var bw = new BinaryWriter(fs))
            {
                byte[] header = new byte[0x2c];
                header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
                header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';
                header[12] = (byte)'f'; header[13] = (byte)'m'; header[14] = (byte)'t'; header[15] = (byte)' ';
                header[16] = 0x10;
                header[20] = 0x01; header[21] = 0x00;
                header[22] = 0x01; header[23] = 0x00;
                header[24] = (byte)(frequency & 0xff);
                header[25] = (byte)((frequency >> 8) & 0xff);
                header[26] = (byte)((frequency >> 16) & 0xff);
                header[27] = (byte)((frequency >> 24) & 0xff);
                int byteRate = frequency * 2;
                header[28] = (byte)(byteRate & 0xff);
                header[29] = (byte)((byteRate >> 8) & 0xff);
                header[30] = (byte)((byteRate >> 16) & 0xff);
                header[31] = (byte)((byteRate >> 24) & 0xff);
                header[32] = 0x02; header[33] = 0x00;
                header[34] = 0x10; header[35] = 0x00;
                header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';

                int fileSize = dataSize + 0x2c - 4;
                header[4] = (byte)(fileSize & 0xff);
                header[5] = (byte)((fileSize >> 8) & 0xff);
                header[6] = (byte)((fileSize >> 16) & 0xff);
                header[7] = (byte)((fileSize >> 24) & 0xff);

                header[40] = (byte)(dataSize & 0xff);
                header[41] = (byte)((dataSize >> 8) & 0xff);
                header[42] = (byte)((dataSize >> 16) & 0xff);
                header[43] = (byte)((dataSize >> 24) & 0xff);

                bw.Write(header);
                bw.Write(wavBuf, 0, dataSize);
            }
        }
    }
}