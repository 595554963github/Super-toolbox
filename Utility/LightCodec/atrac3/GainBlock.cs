namespace LightCodec.atrac3
{
    using AtracGainInfo = LightCodec.atrac3plus.AtracGainInfo;

    public class GainBlock
    {
        public AtracGainInfo[] gBlock = new AtracGainInfo[4];

        public GainBlock()
        {
            for (int i = 0; i < gBlock.Length; i++)
            {
                gBlock[i] = new AtracGainInfo();
            }
        }
    }

}