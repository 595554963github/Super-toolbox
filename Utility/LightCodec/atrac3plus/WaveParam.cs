namespace LightCodec.atrac3plus
{
    public class WaveParam
    {
        internal int freqIndex; //< wave frequency index
        internal int ampSf; //< quantized amplitude scale factor
        internal int ampIndex; //< quantized amplitude index
        internal int phaseIndex; //< quantized phase index

        public virtual void clear()
        {
            freqIndex = 0;
            ampSf = 0;
            ampIndex = 0;
            phaseIndex = 0;
        }
    }

}