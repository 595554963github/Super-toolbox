namespace LightCodec.atrac3plus
{
    public class WaveSynthParams
    {
        public const int ATRAC3P_SUBBANDS = 16; //< number of PQF subbands

        internal bool tonesPresent; //< 1 - tones info present
        internal int amplitudeMode; //< 1 - low range, 0 - high range
        internal int numToneBands; //< number of PQF bands with tones
        internal bool[] toneSharing = new bool[ATRAC3P_SUBBANDS]; //< 1 - subband-wise tone sharing flags
        internal bool[] toneMaster = new bool[ATRAC3P_SUBBANDS]; //< 1 - subband-wise tone channel swapping
        internal bool[] phaseShift = new bool[ATRAC3P_SUBBANDS]; //< 1 - subband-wise 180 degrees phase shifting
        internal int tonesIndex; //< total sum of tones in this unit
        internal WaveParam[] waves = new WaveParam[48];

        public WaveSynthParams()
        {
            for (int i = 0; i < waves.Length; i++)
            {
                waves[i] = new WaveParam();
            }
        }
    }

}