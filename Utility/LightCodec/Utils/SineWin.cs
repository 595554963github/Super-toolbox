using System;

namespace LightCodec.Utils
{
    public class SineWin
    {
        public static readonly float[] ff_sine_64 = new float[64];
        public static readonly float[] ff_sine_128 = new float[128];
        public static readonly float[] ff_sine_512 = new float[512];
        public static readonly float[] ff_sine_1024 = new float[1024];

        private static void sineWindowInit(float[] window)
        {
            int n = window.Length;
            for (int i = 0; i < n; i++)
            {
                window[i] = (float)Math.Sin((i + 0.5) * (Math.PI / (2.0 * n)));
            }
        }

        public static void initFfSineWindows()
        {
            sineWindowInit(ff_sine_64);
            sineWindowInit(ff_sine_128);
            sineWindowInit(ff_sine_512);
            sineWindowInit(ff_sine_1024);
        }
    }

}