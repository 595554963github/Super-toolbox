namespace LightCodec.atrac3
{
    public class ChannelUnit
    {
        public const int SAMPLES_PER_FRAME = 1024;

        public int bandsCoded;
        public int numComponents;
        public float[] prevFrame = new float[SAMPLES_PER_FRAME];
        public int gcBlkSwitch;
        public TonalComponent[] components = new TonalComponent[64];
        public GainBlock[] gainBlock = new GainBlock[2];

        public float[] spectrum = new float[SAMPLES_PER_FRAME];
        public float[] imdctBuf = new float[SAMPLES_PER_FRAME];

        public float[] delayBuf1 = new float[46]; ///<qmf delay buffers
        public float[] delayBuf2 = new float[46];
        public float[] delayBuf3 = new float[46];

        public ChannelUnit()
        {
            for (int i = 0; i < components.Length; i++)
            {
                components[i] = new TonalComponent();
            }
            for (int i = 0; i < gainBlock.Length; i++)
            {
                gainBlock[i] = new GainBlock();
            }
        }
    }

}