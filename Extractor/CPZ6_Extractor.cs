using System.Runtime.InteropServices;
using System.Text;

namespace super_toolbox
{
    public class CPZ6_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        static CPZ6_Extractor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CPZHEADER
        {
            public uint Magic;
            public uint DirCount;
            public uint DirIndexLength;
            public uint FileIndexLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] IndexVerify;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] Md5Data;
            public uint IndexKey;
            public uint IsEncrypt;
            public uint IndexSeed;
            public uint HeaderCRC;
        }

        private struct CMVS_MD5_CTX
        {
            public uint[] buffer;
            public uint[] state;
        }

        private static readonly byte[] ByteString = new byte[96]
        {
            0x89, 0xF0, 0x90, 0xCD, 0x82, 0xB7, 0x82, 0xE9, 0x88, 0xAB, 0x82, 0xA2, 0x8E, 0x71, 0x82, 0xCD,
            0x83, 0x8A, 0x83, 0x52, 0x82, 0xAA, 0x82, 0xA8, 0x8E, 0x64, 0x92, 0x75, 0x82, 0xAB, 0x82, 0xB5,
            0x82, 0xBF, 0x82, 0xE1, 0x82, 0xA2, 0x82, 0xDC, 0x82, 0xB7, 0x81, 0x42, 0x8E, 0xF4, 0x82, 0xED,
            0x82, 0xEA, 0x82, 0xBF, 0x82, 0xE1, 0x82, 0xA2, 0x82, 0xDC, 0x82, 0xB7, 0x82, 0xE6, 0x81, 0x60,
            0x81, 0x41, 0x82, 0xC6, 0x82, 0xA2, 0x82, 0xA4, 0x82, 0xA9, 0x82, 0xE0, 0x82, 0xA4, 0x8E, 0xF4,
            0x82, 0xC1, 0x82, 0xBF, 0x82, 0xE1, 0x82, 0xA2, 0x82, 0xDC, 0x82, 0xB5, 0x82, 0xBD, 0x81, 0xF4
        };

        private static uint Ror(uint n, byte bit)
        {
            return (n << (32 - bit)) | (n >> bit);
        }

        private static uint Rol(uint n, byte bit)
        {
            return (n << bit) | (n >> (32 - bit));
        }

        private static void Swap<T>(ref T a1, ref T a2)
        {
            T temp = a1;
            a1 = a2;
            a2 = temp;
        }

        private static uint F1(uint x, uint y, uint z)
        {
            return z ^ (x & (y ^ z));
        }

        private static uint F2(uint x, uint y, uint z)
        {
            return F1(z, x, y);
        }

        private static uint F3(uint x, uint y, uint z)
        {
            return x ^ y ^ z;
        }

        private static uint F4(uint x, uint y, uint z)
        {
            return y ^ (x | ~z);
        }

        private static void MD5STEP(Func<uint, uint, uint, uint> f, ref uint w, uint x, uint y, uint z, uint input, int shift)
        {
            w += f(x, y, z) + input;
            w = (w << shift) | (w >> (32 - shift));
            w += x;
        }

        private static void MD5Transform(uint[] buf, uint[] input)
        {
            uint a = buf[0];
            uint b = buf[1];
            uint c = buf[2];
            uint d = buf[3];

            MD5STEP(F1, ref a, b, c, d, input[0] + 0xd76aa478, 7);
            MD5STEP(F1, ref d, a, b, c, input[1] + 0xe8c7b756, 12);
            MD5STEP(F1, ref c, d, a, b, input[2] + 0x242070db, 17);
            MD5STEP(F1, ref b, c, d, a, input[3] + 0xc1bdceee, 22);
            MD5STEP(F1, ref a, b, c, d, input[4] + 0xf57c0faf, 7);
            MD5STEP(F1, ref d, a, b, c, input[5] + 0x4787c62a, 12);
            MD5STEP(F1, ref c, d, a, b, input[6] + 0xa8304613, 17);
            MD5STEP(F1, ref b, c, d, a, input[7] + 0xfd469501, 22);
            MD5STEP(F1, ref a, b, c, d, input[8] + 0x698098d8, 7);
            MD5STEP(F1, ref d, a, b, c, input[9] + 0x8b44f7af, 12);
            MD5STEP(F1, ref c, d, a, b, input[10] + 0xffff5bb1, 17);
            MD5STEP(F1, ref b, c, d, a, input[11] + 0x895cd7be, 22);
            MD5STEP(F1, ref a, b, c, d, input[12] + 0x6b901122, 7);
            MD5STEP(F1, ref d, a, b, c, input[13] + 0xfd987193, 12);
            MD5STEP(F1, ref c, d, a, b, input[14] + 0xa679438e, 17);
            MD5STEP(F1, ref b, c, d, a, input[15] + 0x49b40821, 22);
            MD5STEP(F2, ref a, b, c, d, input[1] + 0xf61e2562, 5);
            MD5STEP(F2, ref d, a, b, c, input[6] + 0xc040b340, 9);
            MD5STEP(F2, ref c, d, a, b, input[11] + 0x265e5a51, 14);
            MD5STEP(F2, ref b, c, d, a, input[0] + 0xe9b6c7aa, 20);
            MD5STEP(F2, ref a, b, c, d, input[5] + 0xd62f105d, 5);
            MD5STEP(F2, ref d, a, b, c, input[10] + 0x02441453, 9);
            MD5STEP(F2, ref c, d, a, b, input[15] + 0xd8a1e681, 14);
            MD5STEP(F2, ref b, c, d, a, input[4] + 0xe7d3fbc8, 20);
            MD5STEP(F2, ref a, b, c, d, input[9] + 0x21e1cde6, 5);
            MD5STEP(F2, ref d, a, b, c, input[14] + 0xc33707d6, 9);
            MD5STEP(F2, ref c, d, a, b, input[3] + 0xf4d50d87, 14);
            MD5STEP(F2, ref b, c, d, a, input[8] + 0x455a14ed, 20);
            MD5STEP(F2, ref a, b, c, d, input[13] + 0xa9e3e905, 5);
            MD5STEP(F2, ref d, a, b, c, input[2] + 0xfcefa3f8, 9);
            MD5STEP(F2, ref c, d, a, b, input[7] + 0x676f02d9, 14);
            MD5STEP(F2, ref b, c, d, a, input[12] + 0x8d2a4c8a, 20);
            MD5STEP(F3, ref a, b, c, d, input[5] + 0xfffa3942, 4);
            MD5STEP(F3, ref d, a, b, c, input[8] + 0x8771f681, 11);
            MD5STEP(F3, ref c, d, a, b, input[11] + 0x6d9d6122, 16);
            MD5STEP(F3, ref b, c, d, a, input[14] + 0xfde5380c, 23);
            MD5STEP(F3, ref a, b, c, d, input[1] + 0xa4beea44, 4);
            MD5STEP(F3, ref d, a, b, c, input[4] + 0x4bdecfa9, 11);
            MD5STEP(F3, ref c, d, a, b, input[7] + 0xf6bb4b60, 16);
            MD5STEP(F3, ref b, c, d, a, input[10] + 0xbebfbc70, 23);
            MD5STEP(F3, ref a, b, c, d, input[13] + 0x289b7ec6, 4);
            MD5STEP(F3, ref d, a, b, c, input[0] + 0xeaa127fa, 11);
            MD5STEP(F3, ref c, d, a, b, input[3] + 0xd4ef3085, 16);
            MD5STEP(F3, ref b, c, d, a, input[6] + 0x04881d05, 23);
            MD5STEP(F3, ref a, b, c, d, input[9] + 0xd9d4d039, 4);
            MD5STEP(F3, ref d, a, b, c, input[12] + 0xe6db99e5, 11);
            MD5STEP(F3, ref c, d, a, b, input[15] + 0x1fa27cf8, 16);
            MD5STEP(F3, ref b, c, d, a, input[2] + 0xc4ac5665, 23);
            MD5STEP(F4, ref a, b, c, d, input[0] + 0xf4292244, 6);
            MD5STEP(F4, ref d, a, b, c, input[7] + 0x432aff97, 10);
            MD5STEP(F4, ref c, d, a, b, input[14] + 0xab9423a7, 15);
            MD5STEP(F4, ref b, c, d, a, input[5] + 0xfc93a039, 21);
            MD5STEP(F4, ref a, b, c, d, input[12] + 0x655b59c3, 6);
            MD5STEP(F4, ref d, a, b, c, input[3] + 0x8f0ccc92, 10);
            MD5STEP(F4, ref c, d, a, b, input[10] + 0xffeff47d, 15);
            MD5STEP(F4, ref b, c, d, a, input[1] + 0x85845dd1, 21);
            MD5STEP(F4, ref a, b, c, d, input[8] + 0x6fa87e4f, 6);
            MD5STEP(F4, ref d, a, b, c, input[15] + 0xfe2ce6e0, 10);
            MD5STEP(F4, ref c, d, a, b, input[6] + 0xa3014314, 15);
            MD5STEP(F4, ref b, c, d, a, input[13] + 0x4e0811a1, 21);
            MD5STEP(F4, ref a, b, c, d, input[4] + 0xf7537e82, 6);
            MD5STEP(F4, ref d, a, b, c, input[11] + 0xbd3af235, 10);
            MD5STEP(F4, ref c, d, a, b, input[2] + 0x2ad7d2bb, 15);
            MD5STEP(F4, ref b, c, d, a, input[9] + 0xeb86d391, 21);

            buf[0] += a;
            buf[1] += b;
            buf[2] += c;
            buf[3] += d;
        }

        private static void CMVS_MD5(uint[] data, CMVS_MD5_CTX ctx)
        {
            ctx.state = new uint[4];
            ctx.state[0] = 0xC74A2B01;
            ctx.state[1] = 0xE7C8AB8F;
            ctx.state[2] = 0xD8BEDC4E;
            ctx.state[3] = 0x7302A4C5;

            ctx.buffer = new uint[16];
            Array.Clear(ctx.buffer, 0, ctx.buffer.Length);
            ctx.buffer[0] = data[0];
            ctx.buffer[1] = data[1];
            ctx.buffer[2] = data[2];
            ctx.buffer[3] = data[3];
            ctx.buffer[4] = 0x80;
            ctx.buffer[14] = 16 * 8;
            ctx.buffer[15] = 0;
            MD5Transform(ctx.state, ctx.buffer);
            data[0] = ctx.state[3];
            data[1] = ctx.state[1];
            data[2] = ctx.state[2];
            data[3] = ctx.state[0];
        }

        private static void CpzHeaderDecrypt(ref CPZHEADER header)
        {
            header.DirCount ^= 0xfe3a53da;
            header.DirIndexLength ^= 0x37f298e8;
            header.FileIndexLength ^= 0x7a6f3a2d;
            header.Md5Data[0] ^= 0x43de7c1a;
            header.Md5Data[1] ^= 0xcc65f416;
            header.Md5Data[2] ^= 0xd016a93d;
            header.Md5Data[3] ^= 0x97a3ba9b;
            header.IndexKey ^= 0xae7d39b7;
            header.IsEncrypt ^= 0xfb73a956;
            header.IndexSeed ^= 0x37acf832;

            CMVS_MD5_CTX ctx = new CMVS_MD5_CTX();
            CMVS_MD5(header.Md5Data, ctx);

            Swap(ref header.Md5Data[0], ref header.Md5Data[2]);
            Swap(ref header.Md5Data[2], ref header.Md5Data[3]);
            header.Md5Data[0] ^= 0x45a76c2f;
            header.Md5Data[1] -= 0x5ba17fcb;
            header.Md5Data[2] ^= 0x79abe8ad;
            header.Md5Data[3] -= 0x1c08561b;
            header.IndexSeed = Ror(header.IndexSeed, 5);
            header.IndexSeed *= 0x7da8f173;
            header.IndexSeed += 0x13712765;
        }

        private static uint[] GetIndexTable1(uint indexKey)
        {
            indexKey ^= 0x3795b39a;
            uint[] dwordString = new uint[24];
            Buffer.BlockCopy(ByteString, 0, dwordString, 0, 96);
            for (int i = 0; i < 24; i++) dwordString[i] -= indexKey;
            return dwordString;
        }

        private static byte[] GetByteTable2(uint key, uint seed)
        {
            byte[] byteTable = new byte[256];
            for (int i = 0; i < 256; i++) byteTable[i] = (byte)i;

            for (int i = 0; i < 256; i++)
            {
                byte a = byteTable[(key >> 0x10) & 0xff];
                byte b = byteTable[key & 0xff];
                Swap(ref a, ref b);
                byteTable[(key >> 0x10) & 0xff] = a;
                byteTable[key & 0xff] = b;

                byte c = byteTable[(key >> 0x8) & 0xff];
                byte d = byteTable[key >> 0x18];
                Swap(ref c, ref d);
                byteTable[(key >> 0x8) & 0xff] = c;
                byteTable[key >> 0x18] = d;

                key = Ror(key, 2);
                key *= 0x1a74f195;
                key += seed;
            }
            return byteTable;
        }

        private static byte GetIndexRorBit1(uint indexKey)
        {
            indexKey ^= 0x3795b39a;
            uint temp = indexKey;
            temp >>= 8; temp ^= indexKey;
            temp >>= 8; temp ^= indexKey;
            temp >>= 8; temp ^= indexKey;
            temp ^= 0xfffffffb;
            temp &= 0xf;
            temp += 7;
            return (byte)temp;
        }

        private static void CpzIndexDecrypt1(byte[] buff, uint indexLength, uint indexKey)
        {
            uint[] indexTable1 = GetIndexTable1(indexKey);
            byte rorBit = GetIndexRorBit1(indexKey);
            uint flag = 5;

            for (int i = 0; i < indexLength / 4; i++)
            {
                uint value = BitConverter.ToUInt32(buff, i * 4);
                value ^= indexTable1[(5 + i) % 0x18];
                value += 0x784c5062;
                value = Ror(value, rorBit);
                value += 0x1010101;
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Copy(bytes, 0, buff, i * 4, 4);
                flag++; flag %= 0x18;
            }

            for (int i = (int)(indexLength / 4 * 4); i < indexLength; i++)
            {
                uint temp = indexTable1[flag % 0x18];
                temp >>= 4 * (i % 4);
                buff[i] ^= (byte)temp;
                buff[i] -= 0x7d;
                flag++;
            }
        }

        private static void CpzIndexDecrypt2(byte[] indexBuff, uint indexLength, uint indexKey, uint seed)
        {
            byte[] byteTable = GetByteTable2(indexKey, seed);
            for (int i = 0; i < indexLength; i++)
            {
                indexBuff[i] = byteTable[indexBuff[i] ^ 0x3a];
            }
        }

        private static uint[] GetIndexKey3(CPZHEADER header)
        {
            uint[] key = new uint[4];
            key[0] = header.Md5Data[0] ^ (header.IndexKey + 0x76a3bf29);
            key[1] = header.IndexKey ^ header.Md5Data[1];
            key[2] = header.Md5Data[2] ^ (header.IndexKey + 0x10000000);
            key[3] = header.IndexKey ^ header.Md5Data[3];
            return key;
        }

        private static void CpzIndexDecrypt3(byte[] buff, uint indexLength, uint[] key, uint seed)
        {
            uint flag = 0;

            for (int i = 0; i < indexLength / 4; i++)
            {
                uint value = BitConverter.ToUInt32(buff, i * 4);
                value ^= key[i & 3];
                value -= 0x4a91c262;
                value = Rol(value, 3);
                value -= seed;
                seed += 0x10fb562a;
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Copy(bytes, 0, buff, i * 4, 4);
                flag++; flag &= 3;
            }

            for (int i = (int)(indexLength / 4 * 4); i < indexLength; i++)
            {
                uint temp = key[flag];
                temp >>= 6;
                buff[i] ^= (byte)temp;
                buff[i] += 0x37;
                flag++; flag &= 3;
            }
        }

        private static void CpzFileIndexDecrypt1(byte[] buff, uint length, uint key, uint seed)
        {
            byte[] byteTable = GetByteTable2(key, seed);
            for (int i = 0; i < length; i++)
            {
                buff[i] = byteTable[buff[i] ^ 0x7e];
            }
        }

        private static uint[] GetFileIndexKey2(uint dirKey, CPZHEADER header)
        {
            uint[] key = new uint[4];
            key[0] = dirKey ^ header.Md5Data[0];
            key[2] = dirKey ^ header.Md5Data[2];
            key[1] = (dirKey + 0x11003322) ^ header.Md5Data[1];
            dirKey += 0x34216785;
            key[3] = dirKey ^ header.Md5Data[3];
            return key;
        }

        private static void CpzFileIndexDecrypt2(byte[] fileIndexBuff, uint length, uint dirKey, CPZHEADER header)
        {
            uint[] fileIndexKey = GetFileIndexKey2(dirKey, header);
            uint seed = 0x2a65cb4f;
            uint flag = 0;

            for (int i = 0; i < length / 4; i++)
            {
                uint value = BitConverter.ToUInt32(fileIndexBuff, i * 4);
                value ^= fileIndexKey[i & 3];
                value -= seed;
                value = Rol(value, 2);
                value += 0x37a19e8b;
                seed -= 0x139fa9b;
                byte[] bytes = BitConverter.GetBytes(value);
                Array.Copy(bytes, 0, fileIndexBuff, i * 4, 4);
                flag++; flag &= 3;
            }

            for (int i = (int)(length / 4 * 4); i < length; i++)
            {
                uint temp = fileIndexKey[flag];
                temp >>= 4;
                fileIndexBuff[i] ^= (byte)temp;
                fileIndexBuff[i] += 3;
                flag++; flag &= 3;
            }
        }

        private static void CpzIndexDecrypt(byte[] indexBuff, CPZHEADER header)
        {
            byte[] buff = indexBuff;
            CpzIndexDecrypt1(indexBuff, header.DirIndexLength + header.FileIndexLength, header.IndexKey);
            CpzIndexDecrypt2(indexBuff, header.DirIndexLength, header.IndexKey, header.Md5Data[1]);
            uint[] key = GetIndexKey3(header);
            CpzIndexDecrypt3(indexBuff, header.DirIndexLength, key, 0x76548aef);

            uint flag = 0;
            int buffPos = 0;

            while (flag < header.DirIndexLength)
            {
                uint indexLength = BitConverter.ToUInt32(buff, buffPos); buffPos += 4; flag += 4;
                uint fileCount = BitConverter.ToUInt32(buff, buffPos); buffPos += 4; flag += 4;
                uint fileIndexOffset = BitConverter.ToUInt32(buff, buffPos); buffPos += 4; flag += 4;
                uint dirKey = BitConverter.ToUInt32(buff, buffPos); buffPos += 4; flag += 4;

                buffPos += (int)(indexLength - 16);
                flag += indexLength - 16;

                uint nextFileIndexOffset;
                if (flag < header.DirIndexLength)
                {
                    nextFileIndexOffset = BitConverter.ToUInt32(buff, buffPos + 8);
                    int start = (int)(header.DirIndexLength + fileIndexOffset);
                    int length = (int)(nextFileIndexOffset - fileIndexOffset);
                    byte[] segment = new byte[length];
                    Array.Copy(indexBuff, start, segment, 0, length);
                    CpzFileIndexDecrypt1(segment, (uint)length, header.IndexKey, header.Md5Data[2]);
                    CpzFileIndexDecrypt2(segment, (uint)length, dirKey, header);
                    Array.Copy(segment, 0, indexBuff, start, length);
                }
                else
                {
                    int start = (int)(header.DirIndexLength + fileIndexOffset);
                    int length = (int)(header.FileIndexLength - fileIndexOffset);
                    byte[] segment = new byte[length];
                    Array.Copy(indexBuff, start, segment, 0, length);
                    CpzFileIndexDecrypt1(segment, (uint)length, header.IndexKey, header.Md5Data[2]);
                    CpzFileIndexDecrypt2(segment, (uint)length, dirKey, header);
                    Array.Copy(segment, 0, indexBuff, start, length);
                    break;
                }
            }
        }

        private static void CpzResourceDecrypt(byte[] fileBuff, uint length, uint indexKey, uint[] md5Data, uint seed)
        {
            uint[] decryptKey = new uint[32];
            uint key = md5Data[1] >> 2;

            byte[] byteTable = GetByteTable2(md5Data[3], indexKey);
            byte[] p = new byte[96];
            for (int i = 0; i < 96; i++)
            {
                p[i] = (byte)(key ^ byteTable[ByteString[i] & 0xff]);
            }
            Buffer.BlockCopy(p, 0, decryptKey, 0, 96);
            for (int i = 0; i < 24; i++) decryptKey[i] ^= seed;

            key = 0x2748c39e;
            uint flag = 0x0a;

            for (int i = 0; i < length / 4; i++)
            {
                uint temp = decryptKey[flag];
                temp >>= 1;
                temp ^= decryptKey[(key >> 6) & 0xf];
                temp ^= BitConverter.ToUInt32(fileBuff, i * 4);
                temp -= seed;
                temp ^= md5Data[key & 3];
                byte[] bytes = BitConverter.GetBytes(temp);
                Array.Copy(bytes, 0, fileBuff, i * 4, 4);
                key = key + seed + temp;
                flag++; flag &= 0xf;
            }

            for (int i = (int)(length / 4 * 4); i < length; i++)
            {
                fileBuff[i] = byteTable[fileBuff[i] ^ 0xae];
            }
        }

        private static int GetStringLength(byte[] str, int startPos)
        {
            int len = 0;
            while (startPos + len < str.Length && str[startPos + len] != 0) len++;
            return len;
        }

        private static int CpzResourceRelease(FileStream hFile, byte[] indexBuff, CPZHEADER header, string outputDir, Action<string> onFileExtracted)
        {
            byte[] buff = indexBuff;
            int buffPos = 0;
            int extractedFileCount = 0;

            for (int i = 0; i < header.DirCount; i++)
            {
                uint indexLength = BitConverter.ToUInt32(buff, buffPos); buffPos += 4;
                uint fileCount = BitConverter.ToUInt32(buff, buffPos); buffPos += 4;
                uint fileIndexOffset = BitConverter.ToUInt32(buff, buffPos); buffPos += 4;
                uint dirKey = BitConverter.ToUInt32(buff, buffPos); buffPos += 4;

                byte[] fileIndexBuff = new byte[header.FileIndexLength - fileIndexOffset];
                Array.Copy(indexBuff, (int)(header.DirIndexLength + fileIndexOffset), fileIndexBuff, 0, fileIndexBuff.Length);
                int fileIndexPos = 0;

                for (int j = 0; j < fileCount; j++)
                {
                    uint fileIdxLen = BitConverter.ToUInt32(fileIndexBuff, fileIndexPos); fileIndexPos += 4;
                    ulong offset = BitConverter.ToUInt64(fileIndexBuff, fileIndexPos); fileIndexPos += 8;
                    uint length = BitConverter.ToUInt32(fileIndexBuff, fileIndexPos); fileIndexPos += 4;
                    uint crc = BitConverter.ToUInt32(fileIndexBuff, fileIndexPos); fileIndexPos += 4;
                    uint fileKey = BitConverter.ToUInt32(fileIndexBuff, fileIndexPos); fileIndexPos += 4;

                    int nameLen = GetStringLength(fileIndexBuff, fileIndexPos);
                    string fileName = Encoding.GetEncoding(932).GetString(fileIndexBuff, fileIndexPos, nameLen);
                    fileIndexPos += (int)fileIdxLen - 24;

                    long filePosition = (long)(offset + (ulong)Marshal.SizeOf<CPZHEADER>() + header.DirIndexLength + header.FileIndexLength);
                    hFile.Seek(filePosition, SeekOrigin.Begin);
                    byte[] fileBuff = new byte[length];
                    hFile.Read(fileBuff, 0, (int)length);

                    uint resourceSeed = header.IndexSeed ^ ((header.IndexKey ^ (dirKey + fileKey)) + header.DirCount + 0xa3d61785);
                    CpzResourceDecrypt(fileBuff, length, header.IndexKey, header.Md5Data, resourceSeed);

                    string outputFilePath = Path.Combine(outputDir, fileName);
                    string outputFileDir = Path.GetDirectoryName(outputFilePath) ?? outputDir;
                    Directory.CreateDirectory(outputFileDir);
                    File.WriteAllBytes(outputFilePath, fileBuff);

                    extractedFileCount++;
                    onFileExtracted?.Invoke(outputFilePath);
                }
                buffPos += (int)(indexLength - 16);
            }

            return extractedFileCount;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();
            int totalExtractedCount = 0;

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var cpzFiles = Directory.EnumerateFiles(directoryPath, "*.cpz", SearchOption.AllDirectories);

            int processedFiles = 0;

            foreach (var cpzFile in cpzFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(cpzFile)}");

                try
                {
                    string outputDir = Path.Combine(Path.GetDirectoryName(cpzFile) ?? directoryPath,
                                                   Path.GetFileNameWithoutExtension(cpzFile));
                    Directory.CreateDirectory(outputDir);

                    using (FileStream inFile = new FileStream(cpzFile, FileMode.Open, FileAccess.Read))
                    {
                        byte[] headerBytes = new byte[Marshal.SizeOf<CPZHEADER>()];
                        await inFile.ReadAsync(headerBytes, 0, headerBytes.Length, cancellationToken);

                        CPZHEADER header = new CPZHEADER
                        {
                            IndexVerify = new uint[4],
                            Md5Data = new uint[4]
                        };

                        int offset = 0;
                        header.Magic = BitConverter.ToUInt32(headerBytes, offset); offset += 4;
                        header.DirCount = BitConverter.ToUInt32(headerBytes, offset); offset += 4;
                        header.DirIndexLength = BitConverter.ToUInt32(headerBytes, offset); offset += 4;
                        header.FileIndexLength = BitConverter.ToUInt32(headerBytes, offset); offset += 4;

                        for (int i = 0; i < 4; i++)
                        {
                            header.IndexVerify[i] = BitConverter.ToUInt32(headerBytes, offset);
                            offset += 4;
                        }

                        for (int i = 0; i < 4; i++)
                        {
                            header.Md5Data[i] = BitConverter.ToUInt32(headerBytes, offset);
                            offset += 4;
                        }

                        header.IndexKey = BitConverter.ToUInt32(headerBytes, offset); offset += 4;
                        header.IsEncrypt = BitConverter.ToUInt32(headerBytes, offset); offset += 4;
                        header.IndexSeed = BitConverter.ToUInt32(headerBytes, offset); offset += 4;
                        header.HeaderCRC = BitConverter.ToUInt32(headerBytes, offset);

                        CpzHeaderDecrypt(ref header);

                        byte[] indexBuff = new byte[header.DirIndexLength + header.FileIndexLength];
                        await inFile.ReadAsync(indexBuff, 0, indexBuff.Length, cancellationToken);

                        CpzIndexDecrypt(indexBuff, header);

                        int fileCount = CpzResourceRelease(inFile, indexBuff, header, outputDir, (fileName) =>
                        {
                            OnFileExtracted(fileName);
                            ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(fileName)}");
                        });

                        totalExtractedCount += fileCount;
                        extractedFiles.Add(cpzFile);
                        ExtractionProgress?.Invoke(this, $"已处理:{Path.GetFileName(cpzFile)}");
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{cpzFile}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{cpzFile}时出错:{e.Message}");
                }

                processedFiles++;
            }

            TotalFilesToExtract = totalExtractedCount;

            if (totalExtractedCount > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{totalExtractedCount}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到有效文件");
            }
            OnExtractionCompleted();
        }
    }
}