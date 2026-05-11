using System;

namespace LightCodec.Utils
{
    public class BitReader : IBitReader
    {
        private unsafe void* addr;
        private unsafe void* initialAddr;
        private int initialSize;
        private int size;
        private int bits;
        private int value;
        private int direction;

        public unsafe BitReader(void* addr, int size)
        {
            this.addr = addr;
            this.size = size;
            initialAddr = addr;
            initialSize = size;
            bits = 0;
            direction = 1;
        }

        public virtual bool readBool()
        {
            return read1() != 0;
        }

        public unsafe virtual int read1()
        {
            if (bits <= 0)
            {
                if (size <= 0) return 0;
                value = *((byte*)addr);
                addr = (byte*)addr + direction;
                size--;
                bits = 8;
            }
            int bit = value >> 7;
            bits--;
            value = (value << 1) & 0xFF;
            return bit;
        }

        public virtual int read(int n)
        {
            int result = 0;
            for (int i = 0; i < n; i++)
            {
                result = (result << 1) | read1();
            }
            return result;
        }

        public unsafe virtual int readByte()
        {
            if (bits == 0)
            {
                if (size <= 0) return 0;
                int read = *((byte*)addr);
                addr = (byte*)addr + direction;
                size--;
                return read;
            }
            else
            {
                return read(bits);
            }
        }

        public virtual int BitsLeft
        {
            get { return (size << 3) + bits; }
        }

        public unsafe virtual int BytesRead
        {
            get
            {
                long bytesRead = (byte*)addr - (byte*)initialAddr;
                if (bits == 8)
                {
                    bytesRead--;
                }
                return (int)bytesRead;
            }
        }

        public unsafe virtual int BitsRead
        {
            get
            {
                long byteOffset = (byte*)addr - (byte*)initialAddr;
                return (int)(byteOffset * 8 - bits);
            }
        }

        public virtual int peek(int n)
        {
            int read = this.read(n);
            skip(-n);
            return read;
        }

        public unsafe virtual void skip(int n)
        {
            bits -= n;
            if (n >= 0)
            {
                while (bits < 0)
                {
                    addr = (byte*)addr + direction;
                    size--;
                    bits += 8;
                }
            }
            else
            {
                while (bits > 8)
                {
                    addr = (byte*)addr - direction;
                    size++;
                    bits -= 8;
                }
            }

            if (bits > 0)
            {
                value = *((byte*)addr - direction);
                value = (value << (8 - bits)) & 0xFF;
            }
            else if (bits == 0)
            {
                if (size > 0)
                {
                    value = *((byte*)addr);
                }
            }
        }

        public unsafe virtual void seek(int n)
        {
            addr = (byte*)initialAddr + n;
            size = initialSize - n;
            bits = 0;
        }

        public virtual int Direction
        {
            set
            {
                this.direction = value;
                bits = 0;
            }
        }

        public virtual void byteAlign()
        {
            if (bits > 0 && bits < 8)
            {
                skip(bits);
            }
        }

        public unsafe override string ToString()
        {
            return string.Format("BitReader addr=0x{0:X8}, bits={1:D}, size=0x{2:X}, bits read {3:D}",
                (long)addr, bits, size, BitsRead);
        }
    }
}
