namespace LightCodec.Utils
{
    public interface IBitReader
    {
        int read1();
        bool readBool();
        int read(int n);
        int peek(int n);
        void skip(int n);
    }

}