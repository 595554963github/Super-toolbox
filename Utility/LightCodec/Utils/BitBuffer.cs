namespace LightCodec.Utils
{
    public class BitBuffer : IBitReader
    {
        // Store bits as ints for faster reading
        private readonly int[] bits;
        private int readIndex;
        private int writeIndex;
        private int readCount;
        private int writeCount;

        public BitBuffer(int Length)
        {
            bits = new int[Length];
        }

        public virtual int read1()
        {
            readCount++;
            int bit = bits[readIndex];
            readIndex++;
            if (readIndex >= bits.Length)
            {
                readIndex = 0;
            }

            return bit;
        }

        public virtual int read(int n)
        {
            int value = 0;
            for (; n > 0; n--)
            {
                value = (value << 1) + read1();
            }

            return value;
        }

        public virtual int BitsRead
        {
            get
            {
                return readCount;
            }
        }

        public virtual int BytesRead
        {
            get
            {
                return (int)((uint)BitsRead >> 3);
            }
        }

        public virtual int BitsWritten
        {
            get
            {
                return writeCount;
            }
        }

        public virtual int BytesWritten
        {
            get
            {
                return (int)((uint)BitsWritten >> 3);
            }
        }

        public virtual void skip(int n)
        {
            readCount += n;
            readIndex += n;
            while (readIndex < 0)
            {
                readIndex += bits.Length;
            }
            while (readIndex >= bits.Length)
            {
                readIndex -= bits.Length;
            }
        }

        private void writeBit(int n)
        {
            bits[writeIndex] = n;
            writeIndex++;
            writeCount++;
            if (writeIndex >= bits.Length)
            {
                writeIndex = 0;
            }
        }

        public virtual void writeByte(int n)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                writeBit((n >> bit) & 0x1);
            }
        }

        public virtual bool readBool()
        {
            return read1() != 0;
        }

        public virtual int peek(int n)
        {
            int read = this.read(n);
            skip(-n);
            return read;
        }

        public override string ToString()
        {
            return string.Format("BitBuffer readIndex={0:D}, writeIndex={1:D}, readCount={2:D}", readIndex, writeIndex, readCount);
        }
    }

}