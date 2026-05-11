namespace LightCodec.atrac3plus
{
    public class WaveEnvelope
    {
        internal bool hasStartPoint; //< indicates start point within the GHA window
        internal bool hasStopPoint; //< indicates stop point within the GHA window
        internal int startPos; //< start position expressed in n*4 samples
        internal int stopPos; //< stop  position expressed in n*4 samples

        public virtual void clear()
        {
            hasStartPoint = false;
            hasStopPoint = false;
            startPos = 0;
            stopPos = 0;
        }

        public virtual void copy(WaveEnvelope from)
        {
            this.hasStartPoint = from.hasStartPoint;
            this.hasStopPoint = from.hasStopPoint;
            this.startPos = from.startPos;
            this.stopPos = from.stopPos;
        }
    }

}