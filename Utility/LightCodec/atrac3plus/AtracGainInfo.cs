using System;

namespace LightCodec.atrac3plus
{
    public class AtracGainInfo
    {
        public int numPoints; //< number of gain control points
        public int[] levCode = new int[7]; //< level at corresponding control point
        public int[] locCode = new int[7]; //< location of gain control points

        public virtual void clear()
        {
            numPoints = 0;
            for (int i = 0; i < 7; i++)
            {
                levCode[i] = 0;
                locCode[i] = 0;
            }
        }

        public virtual void copy(AtracGainInfo from)
        {
            this.numPoints = from.numPoints;
            Array.Copy(from.levCode, 0, this.levCode, 0, levCode.Length);
            Array.Copy(from.locCode, 0, this.locCode, 0, locCode.Length);
        }
    }

}