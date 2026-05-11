namespace LightCodec.Utils
{
    internal static class Arrays
    {
        public static T[] CopyOf<T>(T[] original, int newLength)
        {
            T[] dest = new T[newLength];
            System.Array.Copy(original, dest, newLength);
            return dest;
        }

        public static T[] CopyOfRange<T>(T[] original, int fromIndex, int toIndex)
        {
            int length = toIndex - fromIndex;
            T[] dest = new T[length];
            System.Array.Copy(original, fromIndex, dest, 0, length);
            return dest;
        }

        public static void Fill<T>(T[] array, T value)
        {
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }
        }

        public static void Fill<T>(T[] array, int fromIndex, int toIndex, T value)
        {
            for (int i = fromIndex; i < toIndex; i++)
            {
                array[i] = value;
            }
        }

        public static void Fill(float[][][] array, int fromIndex, int toIndex, float value)
        {
            for (int i = fromIndex; i < toIndex; i++)
                for (int j = 0; j < array[i].Length; j++)
                    for (int k = 0; k < array[i][j].Length; k++)
                        array[i][j][k] = value;
        }
    }
}