namespace LightCodec.Utils
{
    public static class RectangularArrays
    {
        public static int[][][] ReturnRectangularIntArray(int size1, int size2, int size3)
        {
            int[][][] newArray = new int[size1][][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new int[size2][];
                if (size3 > -1)
                {
                    for (int array2 = 0; array2 < size2; array2++)
                    {
                        newArray[array1][array2] = new int[size3];
                    }
                }
            }

            return newArray;
        }

        public static int[][] ReturnRectangularIntArray(int size1, int size2)
        {
            int[][] newArray = new int[size1][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new int[size2];
            }

            return newArray;
        }

        public static short[][] ReturnRectangularShortArray(int size1, int size2)
        {
            short[][] newArray = new short[size1][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new short[size2];
            }

            return newArray;
        }

        public static long[][][] ReturnRectangularLongArray(int size1, int size2, int size3)
        {
            long[][][] newArray = new long[size1][][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new long[size2][];
                if (size3 > -1)
                {
                    for (int array2 = 0; array2 < size2; array2++)
                    {
                        newArray[array1][array2] = new long[size3];
                    }
                }
            }

            return newArray;
        }

        public static int[][][][] ReturnRectangularIntArray(int size1, int size2, int size3, int size4)
        {
            int[][][][] newArray = new int[size1][][][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new int[size2][][];
                if (size3 > -1)
                {
                    for (int array2 = 0; array2 < size2; array2++)
                    {
                        newArray[array1][array2] = new int[size3][];
                        if (size4 > -1)
                        {
                            for (int array3 = 0; array3 < size3; array3++)
                            {
                                newArray[array1][array2][array3] = new int[size4];
                            }
                        }
                    }
                }
            }

            return newArray;
        }

        public static short[][][] ReturnRectangularShortArray(int size1, int size2, int size3)
        {
            short[][][] newArray = new short[size1][][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new short[size2][];
                if (size3 > -1)
                {
                    for (int array2 = 0; array2 < size2; array2++)
                    {
                        newArray[array1][array2] = new short[size3];
                    }
                }
            }

            return newArray;
        }

        public static float[][] ReturnRectangularFloatArray(int size1, int size2)
        {
            float[][] newArray = new float[size1][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new float[size2];
            }

            return newArray;
        }

        public static float[][][] ReturnRectangularFloatArray(int size1, int size2, int size3)
        {
            float[][][] newArray = new float[size1][][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new float[size2][];
                if (size3 > -1)
                {
                    for (int array2 = 0; array2 < size2; array2++)
                    {
                        newArray[array1][array2] = new float[size3];
                    }
                }
            }

            return newArray;
        }

        public static float[][][][] ReturnRectangularFloatArray(int size1, int size2, int size3, int size4)
        {
            float[][][][] newArray = new float[size1][][][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new float[size2][][];
                if (size3 > -1)
                {
                    for (int array2 = 0; array2 < size2; array2++)
                    {
                        newArray[array1][array2] = new float[size3][];
                        if (size4 > -1)
                        {
                            for (int array3 = 0; array3 < size3; array3++)
                            {
                                newArray[array1][array2][array3] = new float[size4];
                            }
                        }
                    }
                }
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

        public static sbyte[][] ReturnRectangularSbyteArray(int size1, int size2)
        {
            sbyte[][] newArray = new sbyte[size1][];
            for (int array1 = 0; array1 < size1; array1++)
            {
                newArray[array1] = new sbyte[size2];
            }

            return newArray;
        }
    }
}