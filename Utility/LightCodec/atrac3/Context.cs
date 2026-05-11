using LightCodec.Utils;
using Atrac = LightCodec.atrac3plus.Atrac;
using BitReader = LightCodec.Utils.BitReader;
using FFT = LightCodec.Utils.FFT;

namespace LightCodec.atrac3
{
    public class Context
    {
        public const int SAMPLES_PER_FRAME = 1024;

        public BitReader br = null!;
        public int codingMode;
        public ChannelUnit[] units = new ChannelUnit[2];
        public int channels;
        public int outputChannels;
        public int blockAlign;
        // joint-stereo related variables
        internal int[] matrixCoeffIndexPrev = new int[4];
        internal int[] matrixCoeffIndexNow = new int[4];
        internal int[] matrixCoeffIndexNext = new int[4];
        internal int[] weightingDelay = new int[6];
        // data buffers
        public float[] tempBuf = new float[1070];

        public Atrac gaincCtx = null!;
        public FFT mdctCtx = null!;

        public float[][] samples = RectangularArrays.ReturnRectangularFloatArray(2, SAMPLES_PER_FRAME);
    }

}