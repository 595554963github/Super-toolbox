namespace super_toolbox
{
    public enum UncompressedType
    {
        Normal,
        FadeToCompressed,
        FadeFromCompressed
    }

    public class EaXaEncoder
    {
        private static readonly double[][] XaFiltersOpposite = new double[][]
        {
            new double[] { 0, 0 },
            new double[] { -0.9375, 0 },
            new double[] { -1.796875, 0.8125 },
            new double[] { -1.53125, 0.859375 }
        };

        public int CurrentSample { get; set; }
        public int PreviousSample { get; set; }

        private double _currentQuantError;
        private double _previousQuantError;

        public static short ClipInt16(int a)
        {
            if ((uint)(a + 0x8000) > 0xFFFF)
                return (short)((a >> 31) ^ 0x7FFF);
            else
                return (short)a;
        }

        public void ClearErrors()
        {
            _currentQuantError = 0;
            _previousQuantError = 0;
        }

        public void EncodeSubblock(short[] inputData, int inputOffset, byte[] outputData, int outputOffset, int nSamples, short[]? outputAudioResult = null, int resultOffset = 0)
        {
            double[] maxErrors = new double[4];
            int chosenCoeff = 0;
            double[,] predErrors = new double[4, 30];
            double minError = 1e21;

            int loopCurSample = CurrentSample;
            int loopPrevSample = PreviousSample;

            for (int i = 0; i < 4; i++)
            {
                double maxAbsError = 0;
                int tempCurSample = loopCurSample;
                int tempPrevSample = loopPrevSample;

                for (int j = 0; j < nSamples; j++)
                {
                    int nextSample = inputData[inputOffset + j];
                    double predictionError = tempPrevSample * XaFiltersOpposite[i][1]
                                           + tempCurSample * XaFiltersOpposite[i][0]
                                           + nextSample;
                    predErrors[i, j] = predictionError;
                    double absError = Math.Abs(predictionError);
                    if (maxAbsError < absError)
                        maxAbsError = absError;
                    tempPrevSample = tempCurSample;
                    tempCurSample = nextSample;
                }

                maxErrors[i] = maxAbsError;
                if (minError > maxAbsError)
                {
                    minError = maxAbsError;
                    chosenCoeff = i;
                }
                if (i == 0 && maxErrors[0] <= 7)
                {
                    chosenCoeff = 0;
                    break;
                }
            }

            if (nSamples > 0)
            {
                CurrentSample = inputData[inputOffset + nSamples - 1];
                PreviousSample = nSamples >= 2 ? inputData[inputOffset + nSamples - 2] : CurrentSample;
            }

            int maxError = Math.Min(30000, (int)maxErrors[chosenCoeff]);
            int testShift = 0x4000;
            byte shift;
            for (shift = 0; shift < 12; shift++)
            {
                if ((testShift & (maxError + (testShift >> 3))) != 0) break;
                testShift >>= 1;
            }

            byte coeffHint = (byte)(chosenCoeff << 4);
            outputData[outputOffset] = (byte)((shift & 0x0F) | coeffHint);

            int outPos = outputOffset + 1;
            double coefPrev = XaFiltersOpposite[chosenCoeff][1];
            double coeffCur = XaFiltersOpposite[chosenCoeff][0];
            double shiftMul = 1 << shift;

            for (int i = 0; i < nSamples; i++)
            {
                double predWithQuantizError = coefPrev * _previousQuantError
                                            + coeffCur * _currentQuantError
                                            + predErrors[chosenCoeff, i];
                int quantValue = ((int)Math.Round(predWithQuantizError * shiftMul, MidpointRounding.AwayFromZero) + 0x800) & unchecked((int)0xFFFFF000);
                quantValue = ClipInt16(quantValue);

                if ((i & 1) == 0)
                {
                    outputData[outPos] = (byte)((quantValue >> 8) & 0xF0);
                }
                else
                {
                    outputData[outPos] = (byte)(outputData[outPos] | ((quantValue >> 12) & 0x0F));
                    outPos++;
                }

                _previousQuantError = _currentQuantError;
                _currentQuantError = (quantValue >> shift) - predWithQuantizError;

                if (outputAudioResult != null)
                {
                    outputAudioResult[resultOffset + i] = ClipInt16(inputData[inputOffset + i] + (int)Math.Round(_currentQuantError, MidpointRounding.AwayFromZero));
                }
            }
        }

        public void WriteUncompressedSubblock(short[] inputData, int inputOffset, byte[] outputData, int outputOffset, int nSamples, UncompressedType type)
        {
            int outPos = outputOffset;
            outputData[outPos++] = 0xEE;

            short[] audioToWrite = inputData;
            int audioOffset = inputOffset;
            short[]? transformedAudio = null;

            if (type == UncompressedType.FadeToCompressed)
            {
                CurrentSample = PreviousSample = inputData[inputOffset];
                transformedAudio = new short[28];
                byte[] outputEncoded = new byte[15];
                EncodeSubblock(inputData, inputOffset, outputEncoded, 0, 28, transformedAudio, 0);
                for (int i = 0; i < 28; i++)
                    transformedAudio[i] = (short)(((int)inputData[inputOffset + i] * (28 - i) + (int)transformedAudio[i] * i) / 28);
                audioToWrite = transformedAudio;
                audioOffset = 0;
                ClearErrors();
            }
            else if (type == UncompressedType.FadeFromCompressed)
            {
                transformedAudio = new short[nSamples];
                byte[] outputEncoded = new byte[15];
                EncodeSubblock(inputData, inputOffset, outputEncoded, 0, nSamples, transformedAudio, 0);
                for (int i = 0; i < nSamples; i++)
                    transformedAudio[i] = (short)(((int)transformedAudio[i] * (nSamples - i) + (int)inputData[inputOffset + i] * i) / nSamples);
                audioToWrite = transformedAudio;
                audioOffset = 0;
            }

            if (nSamples == 28)
            {
                CurrentSample = inputData[inputOffset + 27];
                PreviousSample = inputData[inputOffset + 26];
            }
            else
            {
                CurrentSample = PreviousSample = inputData[inputOffset + nSamples - 1];
            }

            Write16BE(outputData, ref outPos, (ushort)CurrentSample);
            Write16BE(outputData, ref outPos, (ushort)PreviousSample);

            int i2;
            for (i2 = 0; i2 < nSamples; i2++)
                Write16BE(outputData, ref outPos, (ushort)audioToWrite[audioOffset + i2]);
            for (; i2 < 28; i2++)
            {
                outputData[outPos++] = (byte)'E';
                outputData[outPos++] = (byte)'G';
            }
        }

        private static void Write16BE(byte[] buf, ref int pos, ushort val)
        {
            buf[pos++] = (byte)(val >> 8);
            buf[pos++] = (byte)(val & 0xFF);
        }
    }
}