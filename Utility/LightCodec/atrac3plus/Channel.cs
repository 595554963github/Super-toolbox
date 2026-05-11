
using static LightCodec.atrac3plus.Atrac3plusData2;
using Atrac3plusData2 = LightCodec.atrac3plus.Atrac3plusData2;

namespace LightCodec.atrac3plus
{
    public class Channel
    {
        public int chNum;
        public int numCodedVals; //< number of transmitted quant unit values
        public int fillMode;
        public int splitPoint;
        public int tableType; //< table type: 0 - tone?, 1- noise?
        public int[] quWordlen = new int[32]; //< array of word lengths for each quant unit
        public int[] quSfIdx = new int[32]; //< array of scale factor indexes for each quant unit
        public int[] quTabIdx = new int[32]; //< array of code table indexes for each quant unit
        public int[] spectrum = new int[2048]; //< decoded IMDCT spectrum
        public int[] powerLevs = new int[5]; //< power compensation levels

        // imdct window shape history (2 frames) for overlapping.
        public bool[][] wndShapeHist; //< IMDCT window shape, 0=sine/1=steep
        public bool[] wndShape; //< IMDCT window shape for current frame
        public bool[] wndShapePrev; //< IMDCT window shape for previous frame

        // gain control data history (2 frames) for overlapping.
        internal AtracGainInfo[][] gainDataHist; //< gain control data for all subbands
        internal AtracGainInfo[] gainData; //< gain control data for next frame
        internal AtracGainInfo[] gainDataPrev; //< gain control data for previous frame
        public int numGainSubbands; //< number of subbands with gain control data

        // tones data history (2 frames) for overlapping.
        internal WavesData[][] tonesInfoHist;
        internal WavesData[] tonesInfo;
        internal WavesData[] tonesInfoPrev;

        public static AtracGainInfo[][] ReturnRectangularAtracGainInfoArray(int size1, int size2)
        {
            AtracGainInfo[][] newArray = new AtracGainInfo[size1][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new AtracGainInfo[size2];
            }

            return newArray;
        }

        public static WavesData[][] ReturnRectangularWavesDataArray(int size1, int size2)
        {
            WavesData[][] newArray = new WavesData[size1][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new WavesData[size2];
            }

            return newArray;
        }

        public static bool[][] ReturnRectangularBoolArray(int size1, int size2)
        {
            bool[][] newArray = new bool[size1][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new bool[size2];
            }

            return newArray;
        }

        public Channel(int chNum)
        {
            this.chNum = chNum;

            wndShapeHist = ReturnRectangularBoolArray(2, ATRAC3P_SUBBANDS);

            gainDataHist = ReturnRectangularAtracGainInfoArray(2, ATRAC3P_SUBBANDS);

            tonesInfoHist = ReturnRectangularWavesDataArray(2, ATRAC3P_SUBBANDS);
            for (int i = 0; i < 2; i++)
            {
                for (int sb = 0; sb < ATRAC3P_SUBBANDS; sb++)
                {
                    gainDataHist[i][sb] = new AtracGainInfo();
                    tonesInfoHist[i][sb] = new WavesData();
                }
            }

            wndShape = wndShapeHist[0];
            wndShapePrev = wndShapeHist[1];
            gainData = gainDataHist[0];
            gainDataPrev = gainDataHist[1];
            tonesInfo = tonesInfoHist[0];
            tonesInfoPrev = tonesInfoHist[1];
        }
    }

}