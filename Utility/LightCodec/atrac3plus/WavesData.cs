namespace LightCodec.atrac3plus
{
    public class WavesData
    {
        internal WaveEnvelope pendEnv; //< pending envelope from the previous frame
        internal WaveEnvelope currEnv; //< group envelope from the current frame
        internal int numWavs; //< number of sine waves in the group
        internal int startIndex; //< start index into global tones table for that subband

        public WavesData()
        {
            pendEnv = new WaveEnvelope();
            currEnv = new WaveEnvelope();
        }

        public virtual void clear()
        {
            pendEnv.clear();
            currEnv.clear();
            numWavs = 0;
            startIndex = 0;
        }

        public virtual void copy(WavesData from)
        {
            this.pendEnv.copy(from.pendEnv);
            this.currEnv.copy(from.currEnv);
            this.numWavs = from.numWavs;
            this.startIndex = from.startIndex;
        }
    }

}