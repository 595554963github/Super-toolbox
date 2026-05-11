using System;

namespace LightCodec.Utils
{
    public class CodecUtils
    {
        // FLT_EPSILON the minimum positive number such that 1.0 + FLT_EPSILON != 1.0
        public const float FLT_EPSILON = 1.19209290E-07F;
        public const float M_SQRT1_2 = 0.707106781186547524401f; // 1/Sqrt(2)
        public static readonly float M_PI = (float)Math.PI;
        public const float M_SQRT2 = 1.41421356237309504880f; // Sqrt(2)
        public static readonly int[] ff_log2_tab = new int[] { 0, 0, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7 };

        private static short convertSampleFloatToInt16(float sample)
        {
            //return (short)(Math.Min(Math.Max((int)(sample * 32768f + 0.5f), -32768), 32767) & 0xFFFF);
            return (short)Math.Round(Math.Clamp(sample, -1.0f, 1.0f) * 32767.0f);
        }

        public static unsafe void writeOutput(float[][] samples, short* output, int numberOfSamples, int decodedChannels, int outputChannels)
        {
            switch (outputChannels)
            {
                case 1:
                    for (int i = 0; i < numberOfSamples; i++)
                    {
                        short sample = convertSampleFloatToInt16(samples[0][i]);
                        *(output + i) = (short)sample;
                    }
                    break;
                case 2:
                    if (decodedChannels == 1)
                    {
                        for (int i = 0; i < numberOfSamples; i++)
                        {
                            short sample = convertSampleFloatToInt16(samples[0][i]);
                            *(output + i * 2) = (short)sample;
                            *(output + i * 2 + 1) = (short)sample;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < numberOfSamples; i++)
                        {
                            short lsample = convertSampleFloatToInt16(samples[0][i]);
                            short rsample = convertSampleFloatToInt16(samples[1][i]);
                            *(output + i * 2) = (short)lsample;
                            *(output + i * 2 + 1) = (short)rsample;
                        }
                    }
                    break;
            }
        }

        public static int numberOfLeadingZeros(int value)
        {
            // Shift right unsigned to work with both positive and negative values
            var uValue = (uint)value;
            int leadingZeros = 0;
            while (uValue != 0)
            {
                uValue = uValue >> 1;
                leadingZeros++;
            }

            return (32 - leadingZeros);
        }

        public static int avLog2(int n)
        {
            if (n == 0)
            {
                return 0;
            }
            return 31 - numberOfLeadingZeros(n);
        }

        private static readonly float log2 = (float)System.Math.Log(2.0);

        public static float log2f(float n)
        {
            return (float)System.Math.Log(n) / log2;
        }

        public static int lrintf(float n)
        {
            return (int)Math.Round(n);
        }

        public static float exp2f(float n)
        {
            return (float)System.Math.Pow(2.0, n);
        }

        public static float sqrtf(float n)
        {
            return (float)System.Math.Sqrt(n);
        }

        public static float cosf(float n)
        {
            return (float)System.Math.Cos(n);
        }

        public static float sinf(float n)
        {
            return (float)System.Math.Sin(n);
        }

        public static float atanf(float n)
        {
            return (float)System.Math.Atan(n);
        }

        public static float atan2f(float y, float x)
        {
            return (float)System.Math.Atan2(y, x);
        }
    }

}