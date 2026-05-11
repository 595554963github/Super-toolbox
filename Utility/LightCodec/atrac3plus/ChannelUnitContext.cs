using LightCodec.Utils;
using static LightCodec.atrac3plus.Atrac3plusData2;
using Atrac3plusData2 = LightCodec.atrac3plus.Atrac3plusData2;

namespace LightCodec.atrac3plus
{
    public class ChannelUnitContext
    {
        // Channel unit variables
        public int unitType; //< unit type (mono/stereo)
        public int numQuantUnits;
        public int numSubbands;
        public int usedQuantUnits; //< number of quant units with coded spectrum
        public int numCodedSubbands; //< number of subbands with coded spectrum
        public bool muteFlag; //< mute flag
        public bool useFullTable; //< 1 - full table list, 0 - restricted one
        public bool noisePresent; //< 1 - global noise info present
        public int noiseLevelIndex; //< global noise level index
        public int noiseTableIndex; //< global noise RNG table index
        public bool[] swapChannels = new bool[ATRAC3P_SUBBANDS]; //< 1 - perform subband-wise channel swapping
        public bool[] negateCoeffs = new bool[ATRAC3P_SUBBANDS]; //< 1 - subband-wise IMDCT coefficients negation
        public Channel[] channels = new Channel[2];

        // Variables related to GHA tones
        public WaveSynthParams[] waveSynthHist = new WaveSynthParams[2]; //< waves synth history for two frames
        public WaveSynthParams wavesInfo;
        public WaveSynthParams wavesInfoPrev;

        public IPQFChannelContext[] ipqfCtx = new IPQFChannelContext[2];

        public float[][] prevBuf = RectangularArrays.ReturnRectangularFloatArray(2, ATRAC3P_FRAME_SAMPLES); //< overlapping buffer

        public class IPQFChannelContext
        {
            public float[][] buf1 = RectangularArrays.ReturnRectangularFloatArray(ATRAC3P_PQF_FIR_LEN * 2, 8);
            public float[][] buf2 = RectangularArrays.ReturnRectangularFloatArray(ATRAC3P_PQF_FIR_LEN * 2, 8);
            public int pos;
        }

        public ChannelUnitContext()
        {
            for (int ch = 0; ch < channels.Length; ch++)
            {
                channels[ch] = new Channel(ch);
            }

            for (int i = 0; i < waveSynthHist.Length; i++)
            {
                waveSynthHist[i] = new WaveSynthParams();
            }
            wavesInfo = waveSynthHist[0];
            wavesInfoPrev = waveSynthHist[1];

            for (int i = 0; i < ipqfCtx.Length; i++)
            {
                ipqfCtx[i] = new IPQFChannelContext();
            }
        }
    }

}