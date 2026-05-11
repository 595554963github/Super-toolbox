using System.Runtime.InteropServices;
using System.Text;

namespace super_toolbox
{
    public class CPZ7_Extractor : BaseExtractor
    {
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct cpz_header
        {
            public uint Magic;
            public uint DirCount;
            public uint DirIndexLength;
            public uint FileIndexLength;
            public uint IndexVerify0;
            public uint IndexVerify1;
            public uint IndexVerify2;
            public uint IndexVerify3;
            public uint Md5Data0;
            public uint Md5Data1;
            public uint Md5Data2;
            public uint Md5Data3;
            public uint IndexKey;
            public uint IsEncrypt;
            public uint IndexSeed;
            public uint unk;
            public uint IndexKeySize;
            public uint HeaderCRC;
        }

        private class MD5_CTX
        {
            public uint[] state = new uint[4];
            public uint[] count = new uint[2];
            public byte[] buffer = new byte[64];
        }

        private class cmvs_md5_ctx
        {
            public uint[] buffer = new uint[16];
            public uint[] state = new uint[4];
        }

        private class Bits
        {
            public byte[]? m_input;
            public byte[]? m_output;
            public uint m_src;
            public uint m_bits;
            public uint m_bit_count;
            public ushort[] lhs = new ushort[512];
            public ushort[] rhs = new ushort[512];
            public ushort token;
        }

        private static readonly byte[] ByteString = new byte[]
        {
            0x89, 0xF0, 0x90, 0xCD, 0x82, 0xB7, 0x82, 0xE9, 0x88, 0xAB, 0x82, 0xA2, 0x8E, 0x71, 0x82, 0xCD,
            0x83, 0x8A, 0x83, 0x52, 0x82, 0xAA, 0x82, 0xA8, 0x8E, 0x64, 0x92, 0x75, 0x82, 0xAB, 0x82, 0xB5,
            0x82, 0xBF, 0x82, 0xE1, 0x82, 0xA2, 0x82, 0xDC, 0x82, 0xB7, 0x81, 0x42, 0x8E, 0xF4, 0x82, 0xED,
            0x82, 0xEA, 0x82, 0xBF, 0x82, 0xE1, 0x82, 0xA2, 0x82, 0xDC, 0x82, 0xB7, 0x82, 0xE6, 0x81, 0x60,
            0x81, 0x41, 0x82, 0xC6, 0x82, 0xA2, 0x82, 0xA4, 0x82, 0xA9, 0x82, 0xE0, 0x82, 0xA4, 0x8E, 0xF4,
            0x82, 0xC1, 0x82, 0xBF, 0x82, 0xE1, 0x82, 0xA2, 0x82, 0xDC, 0x82, 0xB5, 0x82, 0xBD, 0x81, 0xF4
        };

        private const int MAX_PATH = 260;

        static CPZ7_Extractor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var cpzFiles = Directory.GetFiles(directoryPath, "*.cpz", SearchOption.AllDirectories).ToArray();
            int totalCpzFiles = cpzFiles.Length;
            int currentFileIndex = 0;
            int totalExtractedFiles = 0;

            try
            {
                for (int i = 0; i < totalCpzFiles; i++)
                {
                    var cpzFilePath = cpzFiles[i];
                    currentFileIndex = i + 1;
                    ThrowIfCancellationRequested(cancellationToken);

                    ExtractionProgress?.Invoke(this, $"正在处理({currentFileIndex}/{totalCpzFiles}):{Path.GetFileName(cpzFilePath)}");

                    try
                    {
                        int extractedCount = await ExtractCpzFile(cpzFilePath, cancellationToken);

                        if (extractedCount > 0)
                        {
                            totalExtractedFiles += extractedCount;
                            ExtractionProgress?.Invoke(this, $"已处理({currentFileIndex}/{totalCpzFiles}):{Path.GetFileName(cpzFilePath)} -> 提取出{extractedCount}个文件");
                        }
                        else
                        {
                            ExtractionError?.Invoke(this, $"{Path.GetFileName(cpzFilePath)}提取失败");
                            OnExtractionFailed($"{Path.GetFileName(cpzFilePath)}提取失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"提取异常:{ex.Message}");
                        OnExtractionFailed($"{Path.GetFileName(cpzFilePath)}处理错误:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"提取完成,共提取{totalExtractedFiles}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnExtractionFailed($"严重错误:{ex.Message}");
            }
        }

        private async Task<int> ExtractCpzFile(string cpzFilePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                int extractedCount = 0;
                FileStream? src = null;

                try
                {
                    src = new FileStream(cpzFilePath, FileMode.Open, FileAccess.Read);

                    cpz_header CPZ_Header = new cpz_header();
                    byte[] headerBytes = new byte[0x48];
                    if (src.Read(headerBytes, 0, 0x48) != 0x48)
                    {
                        ExtractionError?.Invoke(this, "读取头失败");
                        return 0;
                    }

                    CPZ_Header.Magic = BitConverter.ToUInt32(headerBytes, 0);
                    CPZ_Header.DirCount = BitConverter.ToUInt32(headerBytes, 4);
                    CPZ_Header.DirIndexLength = BitConverter.ToUInt32(headerBytes, 8);
                    CPZ_Header.FileIndexLength = BitConverter.ToUInt32(headerBytes, 12);
                    CPZ_Header.IndexVerify0 = BitConverter.ToUInt32(headerBytes, 16);
                    CPZ_Header.IndexVerify1 = BitConverter.ToUInt32(headerBytes, 20);
                    CPZ_Header.IndexVerify2 = BitConverter.ToUInt32(headerBytes, 24);
                    CPZ_Header.IndexVerify3 = BitConverter.ToUInt32(headerBytes, 28);
                    CPZ_Header.Md5Data0 = BitConverter.ToUInt32(headerBytes, 32);
                    CPZ_Header.Md5Data1 = BitConverter.ToUInt32(headerBytes, 36);
                    CPZ_Header.Md5Data2 = BitConverter.ToUInt32(headerBytes, 40);
                    CPZ_Header.Md5Data3 = BitConverter.ToUInt32(headerBytes, 44);
                    CPZ_Header.IndexKey = BitConverter.ToUInt32(headerBytes, 48);
                    CPZ_Header.IsEncrypt = BitConverter.ToUInt32(headerBytes, 52);
                    CPZ_Header.IndexSeed = BitConverter.ToUInt32(headerBytes, 56);
                    CPZ_Header.unk = BitConverter.ToUInt32(headerBytes, 60);
                    CPZ_Header.IndexKeySize = BitConverter.ToUInt32(headerBytes, 64);
                    CPZ_Header.HeaderCRC = BitConverter.ToUInt32(headerBytes, 68);

                    if (CPZ_Header.Magic != 0x375A5043)
                    {
                        return 0;
                    }

                    byte[] headerForCRC = new byte[0x40];
                    src.Seek(0, SeekOrigin.Begin);
                    int bytesRead = 0;
                    while (bytesRead < 0x40)
                    {
                        int read = src.Read(headerForCRC, bytesRead, 0x40 - bytesRead);
                        if (read == 0) break;
                        bytesRead += read;
                    }
                    uint initCheckCRC = CPZ_Header.IndexKeySize - 0x6DC5A9B4;
                    if (CPZ_Header.HeaderCRC != CheckCRC(headerForCRC, 0x40, initCheckCRC))
                    {
                        ExtractionError?.Invoke(this, "CRC校验失败");
                        return 0;
                    }

                    CPZHeaderDecrypt(ref CPZ_Header);

                    uint dirFileLength = CPZ_Header.DirIndexLength + CPZ_Header.FileIndexLength;
                    uint indexSize = dirFileLength + CPZ_Header.IndexKeySize;

                    if (dirFileLength == 0 || indexSize == 0 || dirFileLength > 0xFFFFFFFF - CPZ_Header.IndexKeySize)
                    {
                        ExtractionError?.Invoke(this, "无效的索引大小");
                        return 0;
                    }

                    src.Seek(0x48, SeekOrigin.Begin);
                    byte[] data = new byte[indexSize];
                    if (src.Read(data, 0, (int)indexSize) != indexSize)
                    {
                        ExtractionError?.Invoke(this, "读取索引失败");
                        return 0;
                    }

                    if (!IndexVerify(data, indexSize, CPZ_Header))
                    {
                        ExtractionError?.Invoke(this, "索引验证失败");
                        return 0;
                    }

                    byte[] indexKey = UnpackIndexKey(data, dirFileLength, CPZ_Header.IndexKeySize);
                    if (indexKey == null)
                    {
                        ExtractionError?.Invoke(this, "解压索引密钥失败");
                        return 0;
                    }

                    uint xorLimit = indexSize < dirFileLength ? indexSize : dirFileLength;
                    for (uint idx = 0; idx < xorLimit; idx++)
                    {
                        data[idx] ^= indexKey[(idx + 3) % 0x3FF];
                    }

                    CPZIndexDecrypt1(data, dirFileLength, CPZ_Header.IndexKey);
                    CPZIndexDecrypt2(data, CPZ_Header.DirIndexLength, CPZ_Header.IndexKey, CPZ_Header.Md5Data1);

                    uint[] Key = GetIndexKey3(CPZ_Header);
                    if (Key == null)
                    {
                        ExtractionError?.Invoke(this, "获取索引密钥失败");
                        return 0;
                    }

                    CPZIndexDecrypt3(data, CPZ_Header.DirIndexLength, Key, 0x76548aef);

                    string? dirName = Path.GetDirectoryName(cpzFilePath);
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(cpzFilePath);
                    string targetDir = Path.Combine(dirName ?? Directory.GetCurrentDirectory(), fileNameWithoutExt);
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    uint iPos = 0;

                    while (iPos + 16 <= CPZ_Header.DirIndexLength && iPos + 16 <= indexSize)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        uint dirIndexLen = BitConverter.ToUInt32(data, (int)iPos);
                        uint dirFileCount = BitConverter.ToUInt32(data, (int)iPos + 4);
                        uint dirFileIndexOffset = BitConverter.ToUInt32(data, (int)iPos + 8);
                        uint dirKey = BitConverter.ToUInt32(data, (int)iPos + 12);

                        byte[] dirNameBytes = new byte[MAX_PATH * 2];
                        Array.Copy(data, (int)(iPos + 16), dirNameBytes, 0, (int)(dirIndexLen - 16));
                        string dirNameStr = Encoding.GetEncoding(932).GetString(dirNameBytes).TrimEnd('\0');

                        string fullDirPath = Path.Combine(targetDir, dirNameStr);
                        if (!string.IsNullOrEmpty(dirNameStr) && !Directory.Exists(fullDirPath))
                        {
                            Directory.CreateDirectory(fullDirPath);
                        }

                        iPos += dirIndexLen;

                        uint fileIndexLength;
                        if (iPos + 16 <= CPZ_Header.DirIndexLength && iPos + 16 <= indexSize)
                        {
                            uint nextDirFileIndexOffset = BitConverter.ToUInt32(data, (int)(iPos + 8));
                            if (nextDirFileIndexOffset >= dirFileIndexOffset)
                            {
                                fileIndexLength = nextDirFileIndexOffset - dirFileIndexOffset;
                            }
                            else
                            {
                                fileIndexLength = CPZ_Header.FileIndexLength - dirFileIndexOffset;
                            }
                        }
                        else
                        {
                            fileIndexLength = CPZ_Header.FileIndexLength - dirFileIndexOffset;
                        }

                        if (fileIndexLength > 0 && dirFileIndexOffset + fileIndexLength <= CPZ_Header.FileIndexLength &&
                            CPZ_Header.DirIndexLength + dirFileIndexOffset + fileIndexLength <= indexSize)
                        {
                            byte[] fileIndexData = new byte[fileIndexLength];
                            Array.Copy(data, (int)(CPZ_Header.DirIndexLength + dirFileIndexOffset),
                                       fileIndexData, 0, (int)fileIndexLength);
                            CPZFileIndexDecrypt1(fileIndexData, fileIndexLength, CPZ_Header.IndexKey, CPZ_Header.Md5Data2);
                            CPZFileIndexDecrypt2(fileIndexData, fileIndexLength, dirKey, CPZ_Header);

                            uint jPos = 0;
                            while (jPos + 28 <= fileIndexLength)
                            {
                                ThrowIfCancellationRequested(cancellationToken);

                                uint fileIndexLen = BitConverter.ToUInt32(fileIndexData, (int)jPos);
                                uint fileOffset = BitConverter.ToUInt32(fileIndexData, (int)jPos + 4);
                                uint fileUnk1 = BitConverter.ToUInt32(fileIndexData, (int)jPos + 8);
                                uint fileLen = BitConverter.ToUInt32(fileIndexData, (int)jPos + 12);
                                uint fileUnk2 = BitConverter.ToUInt32(fileIndexData, (int)jPos + 16);
                                uint fileCRC = BitConverter.ToUInt32(fileIndexData, (int)jPos + 20);
                                uint fileKey = BitConverter.ToUInt32(fileIndexData, (int)jPos + 24);

                                byte[] fileNameBytes = new byte[520];
                                Array.Copy(fileIndexData, (int)(jPos + 28), fileNameBytes, 0, (int)(fileIndexLen - 28));
                                string fileNameStr = Encoding.GetEncoding(932).GetString(fileNameBytes).TrimEnd('\0');

                                if (fileLen > 0)
                                {
                                    extractedCount++;

                                    long actualFileOffset = fileOffset + 0x48 + CPZ_Header.DirIndexLength +
                                                           CPZ_Header.FileIndexLength + CPZ_Header.IndexKeySize;

                                    if (actualFileOffset < src.Length)
                                    {
                                        src.Seek(actualFileOffset, SeekOrigin.Begin);

                                        byte[] fileData = new byte[fileLen];
                                        if (src.Read(fileData, 0, (int)fileLen) == fileLen)
                                        {
                                            if (CPZ_Header.IsEncrypt != 0)
                                            {
                                                uint[] md5Data = new uint[] { CPZ_Header.Md5Data0, CPZ_Header.Md5Data1, CPZ_Header.Md5Data2, CPZ_Header.Md5Data3 };
                                                CPZResourceDecrypt(fileData, fileLen, CPZ_Header.IndexKey, md5Data,
                                                                  CPZ_Header.IndexSeed ^ ((CPZ_Header.IndexKey ^ (dirKey + fileKey)) + CPZ_Header.DirCount + 0xa3c61785));
                                            }

                                            string fullPath = Path.Combine(fullDirPath, fileNameStr);
                                            try
                                            {
                                                string? fullDir = Path.GetDirectoryName(fullPath);
                                                if (!string.IsNullOrEmpty(fullDir) && !Directory.Exists(fullDir))
                                                {
                                                    Directory.CreateDirectory(fullDir);
                                                }
                                                File.WriteAllBytes(fullPath, fileData);
                                                ExtractionProgress?.Invoke(this, $"[{extractedCount}] 提取:{dirNameStr}/{fileNameStr} ({fileLen}字节)");
                                                OnFileExtracted(fullPath);
                                            }
                                            catch (Exception ex)
                                            {
                                                ExtractionError?.Invoke(this, $"写入失败:{ex.Message}");
                                            }
                                        }
                                        else
                                        {
                                            ExtractionError?.Invoke(this, $"读取失败:{fileNameStr}");
                                        }
                                    }
                                    else
                                    {
                                        ExtractionError?.Invoke(this, $"无效的文件偏移:{actualFileOffset}");
                                    }
                                }

                                jPos += fileIndexLen;
                            }
                        }
                    }

                    return extractedCount;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理异常: {ex.Message}");
                    return extractedCount;
                }
                finally
                {
                    src?.Close();
                }
            }, cancellationToken);
        }

        private static uint F(uint x, uint y, uint z) { return (x & y) | (~x & z); }
        private static uint G(uint x, uint y, uint z) { return (x & z) | (y & ~z); }
        private static uint H(uint x, uint y, uint z) { return x ^ y ^ z; }
        private static uint I(uint x, uint y, uint z) { return y ^ (x | ~z); }
        private static uint ROTATE_LEFT(uint x, int n) { return (x << n) | (x >> (32 - n)); }

        private static void MD5_memcpy(byte[] output, byte[] input, uint len)
        {
            for (uint i = 0; i < len; i++)
            {
                if (i < output.Length && i < input.Length)
                    output[i] = input[i];
            }
        }

        private static void MD5_memset(byte[] output, int value, uint len)
        {
            for (uint i = 0; i < len; i++)
            {
                if (i < output.Length)
                    output[i] = (byte)value;
            }
        }

        private static void Encode(byte[] output, uint[] input, uint len)
        {
            for (uint i = 0, j = 0; j < len; i++, j += 4)
            {
                if (j + 3 < output.Length)
                {
                    output[j] = (byte)(input[i] & 0xff);
                    output[j + 1] = (byte)((input[i] >> 8) & 0xff);
                    output[j + 2] = (byte)((input[i] >> 16) & 0xff);
                    output[j + 3] = (byte)((input[i] >> 24) & 0xff);
                }
            }
        }

        private static void Decode(uint[] output, byte[] input, uint len)
        {
            for (uint i = 0, j = 0; j < len; i++, j += 4)
            {
                if (j + 3 < input.Length)
                {
                    output[i] = (uint)input[j] | ((uint)input[j + 1] << 8) |
                               ((uint)input[j + 2] << 16) | ((uint)input[j + 3] << 24);
                }
            }
        }

        private static void MD5Transform(uint[] state, byte[] block)
        {
            uint a = state[0], b = state[1], c = state[2], d = state[3];
            uint[] x = new uint[16];
            Decode(x, block, 64);

            a = ROTATE_LEFT(a + F(b, c, d) + x[0] + 0xd76aa478, 7) + b;
            d = ROTATE_LEFT(d + F(a, b, c) + x[1] + 0xe8c7b756, 12) + a;
            c = ROTATE_LEFT(c + F(d, a, b) + x[2] + 0x242070db, 17) + d;
            b = ROTATE_LEFT(b + F(c, d, a) + x[3] + 0xc1bdceee, 22) + c;
            a = ROTATE_LEFT(a + F(b, c, d) + x[4] + 0xf57c0faf, 7) + b;
            d = ROTATE_LEFT(d + F(a, b, c) + x[5] + 0x4787c62a, 12) + a;
            c = ROTATE_LEFT(c + F(d, a, b) + x[6] + 0xa8304613, 17) + d;
            b = ROTATE_LEFT(b + F(c, d, a) + x[7] + 0xfd469501, 22) + c;
            a = ROTATE_LEFT(a + F(b, c, d) + x[8] + 0x698098d8, 7) + b;
            d = ROTATE_LEFT(d + F(a, b, c) + x[9] + 0x8b44f7af, 12) + a;
            c = ROTATE_LEFT(c + F(d, a, b) + x[10] + 0xffff5bb1, 17) + d;
            b = ROTATE_LEFT(b + F(c, d, a) + x[11] + 0x895cd7be, 22) + c;
            a = ROTATE_LEFT(a + F(b, c, d) + x[12] + 0x6b901122, 7) + b;
            d = ROTATE_LEFT(d + F(a, b, c) + x[13] + 0xfd987193, 12) + a;
            c = ROTATE_LEFT(c + F(d, a, b) + x[14] + 0xa679438e, 17) + d;
            b = ROTATE_LEFT(b + F(c, d, a) + x[15] + 0x49b40821, 22) + c;

            a = ROTATE_LEFT(a + G(b, c, d) + x[1] + 0xf61e2562, 5) + b;
            d = ROTATE_LEFT(d + G(a, b, c) + x[6] + 0xc040b340, 9) + a;
            c = ROTATE_LEFT(c + G(d, a, b) + x[11] + 0x265e5a51, 14) + d;
            b = ROTATE_LEFT(b + G(c, d, a) + x[0] + 0xe9b6c7aa, 20) + c;
            a = ROTATE_LEFT(a + G(b, c, d) + x[5] + 0xd62f105d, 5) + b;
            d = ROTATE_LEFT(d + G(a, b, c) + x[10] + 0x02441453, 9) + a;
            c = ROTATE_LEFT(c + G(d, a, b) + x[15] + 0xd8a1e681, 14) + d;
            b = ROTATE_LEFT(b + G(c, d, a) + x[4] + 0xe7d3fbc8, 20) + c;
            a = ROTATE_LEFT(a + G(b, c, d) + x[9] + 0x21e1cde6, 5) + b;
            d = ROTATE_LEFT(d + G(a, b, c) + x[14] + 0xc33707d6, 9) + a;
            c = ROTATE_LEFT(c + G(d, a, b) + x[3] + 0xf4d50d87, 14) + d;
            b = ROTATE_LEFT(b + G(c, d, a) + x[8] + 0x455a14ed, 20) + c;
            a = ROTATE_LEFT(a + G(b, c, d) + x[13] + 0xa9e3e905, 5) + b;
            d = ROTATE_LEFT(d + G(a, b, c) + x[2] + 0xfcefa3f8, 9) + a;
            c = ROTATE_LEFT(c + G(d, a, b) + x[7] + 0x676f02d9, 14) + d;
            b = ROTATE_LEFT(b + G(c, d, a) + x[12] + 0x8d2a4c8a, 20) + c;

            a = ROTATE_LEFT(a + H(b, c, d) + x[5] + 0xfffa3942, 4) + b;
            d = ROTATE_LEFT(d + H(a, b, c) + x[8] + 0x8771f681, 11) + a;
            c = ROTATE_LEFT(c + H(d, a, b) + x[11] + 0x6d9d6122, 16) + d;
            b = ROTATE_LEFT(b + H(c, d, a) + x[14] + 0xfde5380c, 23) + c;
            a = ROTATE_LEFT(a + H(b, c, d) + x[1] + 0xa4beea44, 4) + b;
            d = ROTATE_LEFT(d + H(a, b, c) + x[4] + 0x4bdecfa9, 11) + a;
            c = ROTATE_LEFT(c + H(d, a, b) + x[7] + 0xf6bb4b60, 16) + d;
            b = ROTATE_LEFT(b + H(c, d, a) + x[10] + 0xbebfbc70, 23) + c;
            a = ROTATE_LEFT(a + H(b, c, d) + x[13] + 0x289b7ec6, 4) + b;
            d = ROTATE_LEFT(d + H(a, b, c) + x[0] + 0xeaa127fa, 11) + a;
            c = ROTATE_LEFT(c + H(d, a, b) + x[3] + 0xd4ef3085, 16) + d;
            b = ROTATE_LEFT(b + H(c, d, a) + x[6] + 0x04881d05, 23) + c;
            a = ROTATE_LEFT(a + H(b, c, d) + x[9] + 0xd9d4d039, 4) + b;
            d = ROTATE_LEFT(d + H(a, b, c) + x[12] + 0xe6db99e5, 11) + a;
            c = ROTATE_LEFT(c + H(d, a, b) + x[15] + 0x1fa27cf8, 16) + d;
            b = ROTATE_LEFT(b + H(c, d, a) + x[2] + 0xc4ac5665, 23) + c;

            a = ROTATE_LEFT(a + I(b, c, d) + x[0] + 0xf4292244, 6) + b;
            d = ROTATE_LEFT(d + I(a, b, c) + x[7] + 0x432aff97, 10) + a;
            c = ROTATE_LEFT(c + I(d, a, b) + x[14] + 0xab9423a7, 15) + d;
            b = ROTATE_LEFT(b + I(c, d, a) + x[5] + 0xfc93a039, 21) + c;
            a = ROTATE_LEFT(a + I(b, c, d) + x[12] + 0x655b59c3, 6) + b;
            d = ROTATE_LEFT(d + I(a, b, c) + x[3] + 0x8f0ccc92, 10) + a;
            c = ROTATE_LEFT(c + I(d, a, b) + x[10] + 0xffeff47d, 15) + d;
            b = ROTATE_LEFT(b + I(c, d, a) + x[1] + 0x85845dd1, 21) + c;
            a = ROTATE_LEFT(a + I(b, c, d) + x[8] + 0x6fa87e4f, 6) + b;
            d = ROTATE_LEFT(d + I(a, b, c) + x[15] + 0xfe2ce6e0, 10) + a;
            c = ROTATE_LEFT(c + I(d, a, b) + x[6] + 0xa3014314, 15) + d;
            b = ROTATE_LEFT(b + I(c, d, a) + x[13] + 0x4e0811a1, 21) + c;
            a = ROTATE_LEFT(a + I(b, c, d) + x[4] + 0xf7537e82, 6) + b;
            d = ROTATE_LEFT(d + I(a, b, c) + x[11] + 0xbd3af235, 10) + a;
            c = ROTATE_LEFT(c + I(d, a, b) + x[2] + 0x2ad7d2bb, 15) + d;
            b = ROTATE_LEFT(b + I(c, d, a) + x[9] + 0xeb86d391, 21) + c;

            state[0] += a;
            state[1] += b;
            state[2] += c;
            state[3] += d;
            MD5_memset(new byte[x.Length * 4], 0, 64);
        }

        private static void MD5Init(MD5_CTX context)
        {
            context.count[0] = context.count[1] = 0;
            context.state[0] = 0x67452301;
            context.state[1] = 0xefcdab89;
            context.state[2] = 0x98badcfe;
            context.state[3] = 0x10325476;
        }

        private static void MD5Update(MD5_CTX context, byte[] input, uint inputLen)
        {
            uint i, index, partLen;
            index = (context.count[0] >> 3) & 0x3F;
            if ((context.count[0] += (inputLen << 3)) < (inputLen << 3)) context.count[1]++;
            context.count[1] += (inputLen >> 29);
            partLen = 64 - index;
            if (inputLen >= partLen)
            {
                Array.Copy(input, 0, context.buffer, index, partLen);
                MD5Transform(context.state, context.buffer);
                for (i = partLen; i + 63 < inputLen; i += 64)
                {
                    byte[] tempBlock = new byte[64];
                    Array.Copy(input, i, tempBlock, 0, 64);
                    MD5Transform(context.state, tempBlock);
                }
                index = 0;
            }
            else i = 0;
            Array.Copy(input, i, context.buffer, index, inputLen - i);
        }

        private static void MD5Final(byte[] digest, MD5_CTX context)
        {
            byte[] bits = new byte[8];
            uint index, padLen;
            byte[] PADDING = new byte[64];
            PADDING[0] = 0x80;
            Encode(bits, context.count, 8);
            index = (context.count[0] >> 3) & 0x3f;
            padLen = (index < 56) ? (56 - index) : (120 - index);
            MD5Update(context, PADDING, padLen);
            MD5Update(context, bits, 8);
            Encode(digest, context.state, 16);
        }

        private static uint F1(uint x, uint y, uint z) { return z ^ (x & (y ^ z)); }
        private static uint F2(uint x, uint y, uint z) { return F1(z, x, y); }
        private static uint F3(uint x, uint y, uint z) { return x ^ y ^ z; }
        private static uint F4(uint x, uint y, uint z) { return y ^ (x | ~z); }
        private static uint MD5STEP_ROT(uint x, int n) { return (x << n) | (x >> (32 - n)); }

        private static void MD5Transform2(uint[] buf, uint[] input)
        {
            uint a = buf[0], b = buf[1], c = buf[2], d = buf[3];

            a = MD5STEP_ROT(a + F1(b, c, d) + input[0] + 0xd76aa478, 7) + b;
            d = MD5STEP_ROT(d + F1(a, b, c) + input[1] + 0xe8c7b756, 12) + a;
            c = MD5STEP_ROT(c + F1(d, a, b) + input[2] + 0x242070db, 17) + d;
            b = MD5STEP_ROT(b + F1(c, d, a) + input[3] + 0xc1bdceee, 22) + c;
            a = MD5STEP_ROT(a + F1(b, c, d) + input[4] + 0xf57c0faf, 7) + b;
            d = MD5STEP_ROT(d + F1(a, b, c) + input[5] + 0x4787c62a, 12) + a;
            c = MD5STEP_ROT(c + F1(d, a, b) + input[6] + 0xa8304613, 17) + d;
            b = MD5STEP_ROT(b + F1(c, d, a) + input[7] + 0xfd469501, 22) + c;
            a = MD5STEP_ROT(a + F1(b, c, d) + input[8] + 0x698098d8, 7) + b;
            d = MD5STEP_ROT(d + F1(a, b, c) + input[9] + 0x8b44f7af, 12) + a;
            c = MD5STEP_ROT(c + F1(d, a, b) + input[10] + 0xffff5bb1, 17) + d;
            b = MD5STEP_ROT(b + F1(c, d, a) + input[11] + 0x895cd7be, 22) + c;
            a = MD5STEP_ROT(a + F1(b, c, d) + input[12] + 0x6b901122, 7) + b;
            d = MD5STEP_ROT(d + F1(a, b, c) + input[13] + 0xfd987193, 12) + a;
            c = MD5STEP_ROT(c + F1(d, a, b) + input[14] + 0xa679438e, 17) + d;
            b = MD5STEP_ROT(b + F1(c, d, a) + input[15] + 0x49b40821, 22) + c;

            a = MD5STEP_ROT(a + F2(b, c, d) + input[1] + 0xf61e2562, 5) + b;
            d = MD5STEP_ROT(d + F2(a, b, c) + input[6] + 0xc040b340, 9) + a;
            c = MD5STEP_ROT(c + F2(d, a, b) + input[11] + 0x265e5a51, 14) + d;
            b = MD5STEP_ROT(b + F2(c, d, a) + input[0] + 0xe9b6c7aa, 20) + c;
            a = MD5STEP_ROT(a + F2(b, c, d) + input[5] + 0xd62f105d, 5) + b;
            d = MD5STEP_ROT(d + F2(a, b, c) + input[10] + 0x02441453, 9) + a;
            c = MD5STEP_ROT(c + F2(d, a, b) + input[15] + 0xd8a1e681, 14) + d;
            b = MD5STEP_ROT(b + F2(c, d, a) + input[4] + 0xe7d3fbc8, 20) + c;
            a = MD5STEP_ROT(a + F2(b, c, d) + input[9] + 0x21e1cde6, 5) + b;
            d = MD5STEP_ROT(d + F2(a, b, c) + input[14] + 0xc33707d6, 9) + a;
            c = MD5STEP_ROT(c + F2(d, a, b) + input[3] + 0xf4d50d87, 14) + d;
            b = MD5STEP_ROT(b + F2(c, d, a) + input[8] + 0x455a14ed, 20) + c;
            a = MD5STEP_ROT(a + F2(b, c, d) + input[13] + 0xa9e3e905, 5) + b;
            d = MD5STEP_ROT(d + F2(a, b, c) + input[2] + 0xfcefa3f8, 9) + a;
            c = MD5STEP_ROT(c + F2(d, a, b) + input[7] + 0x676f02d9, 14) + d;
            b = MD5STEP_ROT(b + F2(c, d, a) + input[12] + 0x8d2a4c8a, 20) + c;

            a = MD5STEP_ROT(a + F3(b, c, d) + input[5] + 0xfffa3942, 4) + b;
            d = MD5STEP_ROT(d + F3(a, b, c) + input[8] + 0x8771f681, 11) + a;
            c = MD5STEP_ROT(c + F3(d, a, b) + input[11] + 0x6d9d6122, 16) + d;
            b = MD5STEP_ROT(b + F3(c, d, a) + input[14] + 0xfde5380c, 23) + c;
            a = MD5STEP_ROT(a + F3(b, c, d) + input[1] + 0xa4beea44, 4) + b;
            d = MD5STEP_ROT(d + F3(a, b, c) + input[4] + 0x4bdecfa9, 11) + a;
            c = MD5STEP_ROT(c + F3(d, a, b) + input[7] + 0xf6bb4b60, 16) + d;
            b = MD5STEP_ROT(b + F3(c, d, a) + input[10] + 0xbebfbc70, 23) + c;
            a = MD5STEP_ROT(a + F3(b, c, d) + input[13] + 0x289b7ec6, 4) + b;
            d = MD5STEP_ROT(d + F3(a, b, c) + input[0] + 0xeaa127fa, 11) + a;
            c = MD5STEP_ROT(c + F3(d, a, b) + input[3] + 0xd4ef3085, 16) + d;
            b = MD5STEP_ROT(b + F3(c, d, a) + input[6] + 0x04881d05, 23) + c;
            a = MD5STEP_ROT(a + F3(b, c, d) + input[9] + 0xd9d4d039, 4) + b;
            d = MD5STEP_ROT(d + F3(a, b, c) + input[12] + 0xe6db99e5, 11) + a;
            c = MD5STEP_ROT(c + F3(d, a, b) + input[15] + 0x1fa27cf8, 16) + d;
            b = MD5STEP_ROT(b + F3(c, d, a) + input[2] + 0xc4ac5665, 23) + c;

            a = MD5STEP_ROT(a + F4(b, c, d) + input[0] + 0xf4292244, 6) + b;
            d = MD5STEP_ROT(d + F4(a, b, c) + input[7] + 0x432aff97, 10) + a;
            c = MD5STEP_ROT(c + F4(d, a, b) + input[14] + 0xab9423a7, 15) + d;
            b = MD5STEP_ROT(b + F4(c, d, a) + input[5] + 0xfc93a039, 21) + c;
            a = MD5STEP_ROT(a + F4(b, c, d) + input[12] + 0x655b59c3, 6) + b;
            d = MD5STEP_ROT(d + F4(a, b, c) + input[3] + 0x8f0ccc92, 10) + a;
            c = MD5STEP_ROT(c + F4(d, a, b) + input[10] + 0xffeff47d, 15) + d;
            b = MD5STEP_ROT(b + F4(c, d, a) + input[1] + 0x85845dd1, 21) + c;
            a = MD5STEP_ROT(a + F4(b, c, d) + input[8] + 0x6fa87e4f, 6) + b;
            d = MD5STEP_ROT(d + F4(a, b, c) + input[15] + 0xfe2ce6e0, 10) + a;
            c = MD5STEP_ROT(c + F4(d, a, b) + input[6] + 0xa3014314, 15) + d;
            b = MD5STEP_ROT(b + F4(c, d, a) + input[13] + 0x4e0811a1, 21) + c;
            a = MD5STEP_ROT(a + F4(b, c, d) + input[4] + 0xf7537e82, 6) + b;
            d = MD5STEP_ROT(d + F4(a, b, c) + input[11] + 0xbd3af235, 10) + a;
            c = MD5STEP_ROT(c + F4(d, a, b) + input[2] + 0x2ad7d2bb, 15) + d;
            b = MD5STEP_ROT(b + F4(c, d, a) + input[9] + 0xeb86d391, 21) + c;

            buf[0] += a; buf[1] += b; buf[2] += c; buf[3] += d;
        }

        private static void cmvs_md5(uint[] data, cmvs_md5_ctx ctx)
        {
            ctx.state[0] = 0xC74A2B02;
            ctx.state[1] = 0xE7C8AB8F;
            ctx.state[2] = 0x38BEBC4E;
            ctx.state[3] = 0x7531A4C3;
            Array.Clear(ctx.buffer, 0, ctx.buffer.Length);
            ctx.buffer[0] = data[0];
            ctx.buffer[1] = data[1];
            ctx.buffer[2] = data[2];
            ctx.buffer[3] = data[3];
            ctx.buffer[4] = 0x80;
            ctx.buffer[14] = 16 * 8;
            ctx.buffer[15] = 0;
            MD5Transform2(ctx.state, ctx.buffer);
            data[0] = ctx.state[2];
            data[1] = ctx.state[1];
            data[2] = ctx.state[0];
            data[3] = ctx.state[3];
        }

        private static int GetBits(Bits h_bits, int count)
        {
            int bits = 0;
            while (count-- > 0)
            {
                if (0 == h_bits.m_bit_count)
                {
                    byte[] input = h_bits.m_input ?? throw new InvalidOperationException("输入数据为空");
                    h_bits.m_bits = BitConverter.ToUInt32(input, (int)h_bits.m_src);
                    h_bits.m_src += 4;
                    h_bits.m_bit_count = 32;
                }
                bits = bits << 1 | (int)(h_bits.m_bits & 1);
                h_bits.m_bits >>= 1;
                --h_bits.m_bit_count;
            }
            return bits;
        }

        private static ushort CreateTree(Bits h_bits)
        {
            if (0 != GetBits(h_bits, 1))
            {
                ushort v = h_bits.token++;
                if (v >= 511)
                {
                    throw new InvalidOperationException("Huffman树溢出");
                }
                h_bits.lhs[v] = CreateTree(h_bits);
                h_bits.rhs[v] = CreateTree(h_bits);
                return v;
            }
            else return (ushort)GetBits(h_bits, 8);
        }

        private static void Unpack(Bits h_bits, uint dst_length)
        {
            uint dst = 0;
            h_bits.token = 256;
            ushort root = CreateTree(h_bits);
            byte[] output = h_bits.m_output ?? throw new InvalidOperationException("输出数据为空");
            while (dst < dst_length)
            {
                ushort symbol = root;
                while (symbol >= 0x100)
                {
                    if (0 != GetBits(h_bits, 1)) symbol = h_bits.rhs[symbol];
                    else symbol = h_bits.lhs[symbol];
                }
                output[dst++] = (byte)symbol;
            }
        }

        private static void HuffmanDecoder(byte[] src, uint index, uint length, uint dst_length, byte[] dst)
        {
            Bits h_bits = new Bits();
            h_bits.m_input = src;
            h_bits.m_output = dst;
            h_bits.m_src = index;
            h_bits.m_bit_count = 0;
            h_bits.token = 256;
            Unpack(h_bits, dst_length);
        }

        private static uint Ror(uint N, byte Bit) { return (N << (32 - Bit)) + (N >> Bit); }
        private static uint Rol(uint N, byte Bit) { return (N << Bit) + (N >> (32 - Bit)); }

        private static uint CheckCRC(byte[] data, uint len, uint crc)
        {
            uint k = 0, i = 0, count = len / 4;
            for (k = 0; k < count; k++)
            {
                uint val = BitConverter.ToUInt32(data, (int)k * 4);
                crc += val;
            }
            for (i = 0; i < (len & 3); i++)
            {
                crc += data[count * 4 + i];
            }
            return crc;
        }

        private static void CPZHeaderDecrypt(ref cpz_header CPZ_Header)
        {
            CPZ_Header.DirCount ^= 0xFE3A53DA;
            CPZ_Header.DirIndexLength ^= 0x37F298E8;
            CPZ_Header.FileIndexLength ^= 0x7A6F3A2D;
            CPZ_Header.Md5Data0 ^= 0x43DE7C1A;
            CPZ_Header.Md5Data1 ^= 0xCC65F416;
            CPZ_Header.Md5Data2 ^= 0xD016A93D;
            CPZ_Header.Md5Data3 ^= 0x97A3BA9B;
            CPZ_Header.IndexKey ^= 0xAE7D39B7;
            CPZ_Header.IsEncrypt ^= 0xFB73A956;
            CPZ_Header.IndexSeed ^= 0x37ACF832;
            CPZ_Header.IndexSeed = Ror(CPZ_Header.IndexSeed, 5);
            CPZ_Header.IndexSeed *= 0x7DA8F173;
            CPZ_Header.IndexSeed += 0x13712765;
            cmvs_md5_ctx CTX = new cmvs_md5_ctx();
            uint[] md5Data = new uint[] { CPZ_Header.Md5Data0, CPZ_Header.Md5Data1, CPZ_Header.Md5Data2, CPZ_Header.Md5Data3 };
            cmvs_md5(md5Data, CTX);
            CPZ_Header.Md5Data0 = md5Data[0];
            CPZ_Header.Md5Data1 = md5Data[1];
            CPZ_Header.Md5Data2 = md5Data[2];
            CPZ_Header.Md5Data3 = md5Data[3];
            CPZ_Header.Md5Data0 ^= 0x53A76D2E;
            CPZ_Header.Md5Data1 += 0x5BB17FDA;
            CPZ_Header.Md5Data2 += 0x6853E14D;
            CPZ_Header.Md5Data3 ^= 0xF5C6A9A3;
            CPZ_Header.IndexKeySize ^= 0x65EF99F3;
        }

        private static bool IndexVerify(byte[] data, uint len, cpz_header CPZ_Header)
        {
            byte[] digest = new byte[16];
            uint[] verify = new uint[4];
            MD5_CTX CTX = new MD5_CTX();
            MD5Init(CTX);
            MD5Update(CTX, data, len);
            MD5Final(digest, CTX);
            Buffer.BlockCopy(digest, 0, verify, 0, 16);
            if (CPZ_Header.IndexVerify0 != verify[0] || CPZ_Header.IndexVerify1 != verify[1] || CPZ_Header.IndexVerify2 != verify[2] || CPZ_Header.IndexVerify3 != verify[3]) return false;

            MD5Init(CTX);
            uint tempOffset = CPZ_Header.DirIndexLength + CPZ_Header.FileIndexLength + 0x10;
            uint tempLength = CPZ_Header.IndexKeySize - 0x10;
            byte[] tempData = new byte[tempLength];
            Array.Copy(data, (int)tempOffset, tempData, 0, (int)tempLength);
            MD5Update(CTX, tempData, tempLength);
            MD5Final(digest, CTX);
            Buffer.BlockCopy(digest, 0, verify, 0, 16);

            byte[] verifyIndexKeyData = new byte[16];
            Array.Copy(data, (int)(CPZ_Header.DirIndexLength + CPZ_Header.FileIndexLength), verifyIndexKeyData, 0, 16);
            uint[] verifyIndexKey = new uint[4];
            Buffer.BlockCopy(verifyIndexKeyData, 0, verifyIndexKey, 0, 16);

            return (verifyIndexKey[0] == verify[0] && verifyIndexKey[1] == verify[1] && verifyIndexKey[2] == verify[2] && verifyIndexKey[3] == verify[3]);
        }

        private static byte[] UnpackIndexKey(byte[] srcData, uint offset, uint length)
        {
            uint keyOffset = offset + 0x14;
            uint packedOffset = offset + 0x18;
            uint packedLength = length - 0x18;
            uint unpackedLength = BitConverter.ToUInt32(srcData, (int)(offset + 0x10));

            for (uint i = 0; i < packedLength; i++)
            {
                srcData[packedOffset + i] ^= srcData[keyOffset + (i & 3)];
            }

            byte[] dstData = new byte[unpackedLength];
            HuffmanDecoder(srcData, packedOffset, packedLength, unpackedLength, dstData);
            return dstData;
        }

        private static uint[] GetIndexTable1(uint IndexKey)
        {
            IndexKey ^= 0x3795b39a;
            uint[] DwordString = new uint[24];
            for (uint i = 0; i < 24; i++)
            {
                DwordString[i] = BitConverter.ToUInt32(ByteString, (int)i * 4);
                DwordString[i] -= IndexKey;
            }
            return DwordString;
        }

        private static byte GetIndexRorBit1(uint IndexKey)
        {
            IndexKey ^= 0x3795b39a;
            uint Temp = IndexKey;
            Temp >>= 8; Temp ^= IndexKey;
            Temp >>= 8; Temp ^= IndexKey;
            Temp >>= 8; Temp ^= IndexKey;
            Temp ^= 0xfffffffb;
            Temp &= 0xf;
            Temp += 7;
            return (byte)Temp;
        }

        private static void CPZIndexDecrypt1(byte[] Buff, uint IndexLength, uint IndexKey)
        {
            uint[] IndexTable1 = GetIndexTable1(IndexKey);
            if (IndexTable1 == null) return;
            byte RorBit = GetIndexRorBit1(IndexKey);
            uint Flag = 5;
            uint i;

            for (i = 0; i < IndexLength / 4; i++)
            {
                uint val = BitConverter.ToUInt32(Buff, (int)i * 4);
                val ^= IndexTable1[(5 + i) % 0x18];
                val += 0x784c5062;
                val = Ror(val, RorBit);
                val += 0x1010101;
                byte[] bytes = BitConverter.GetBytes(val);
                Array.Copy(bytes, 0, Buff, (int)i * 4, 4);
                Flag++; Flag %= 0x18;
            }

            for (i = IndexLength / 4 * 4; i < IndexLength; i++)
            {
                uint Temp = IndexTable1[Flag % 0x18];
                Temp >>= (int)(4 * (i % 4));
                Buff[i] ^= (byte)Temp;
                Buff[i] -= 0x7d;
                Flag++;
            }
        }

        private static byte[] GetByteTable2(uint Key, uint Seed)
        {
            byte[] ByteTable = new byte[0x100];
            for (uint i = 0; i < 0x100; i++) ByteTable[i] = (byte)i;
            byte temp;
            for (uint i = 0; i < 0x100; i++)
            {
                temp = ByteTable[(Key >> 0x10) & 0xff];
                ByteTable[(Key >> 0x10) & 0xff] = ByteTable[Key & 0xff];
                ByteTable[Key & 0xff] = temp;
                temp = ByteTable[(Key >> 0x8) & 0xff];
                ByteTable[(Key >> 0x8) & 0xff] = ByteTable[Key >> 0x18];
                ByteTable[Key >> 0x18] = temp;
                Key = Ror(Key, 2);
                Key *= 0x1a74f195;
                Key += Seed;
            }
            return ByteTable;
        }

        private static void CPZIndexDecrypt2(byte[] IndexBuff, uint IndexLength, uint IndexKey, uint Seed)
        {
            byte[] ByteTable = GetByteTable2(IndexKey, Seed);
            if (ByteTable == null) return;
            for (uint i = 0; i < IndexLength; i++) IndexBuff[i] = ByteTable[IndexBuff[i] ^ 0x3a];
        }

        private static uint[] GetIndexKey3(cpz_header CPZ_Header)
        {
            uint[] Key = new uint[4];
            Key[0] = CPZ_Header.Md5Data0 ^ (CPZ_Header.IndexKey + 0x76a3bf29);
            Key[1] = CPZ_Header.IndexKey ^ CPZ_Header.Md5Data1;
            Key[2] = CPZ_Header.Md5Data2 ^ (CPZ_Header.IndexKey + 0x10000000);
            Key[3] = CPZ_Header.IndexKey ^ CPZ_Header.Md5Data3;
            return Key;
        }

        private static void CPZIndexDecrypt3(byte[] Buff, uint IndexLength, uint[] Key, uint Seed)
        {
            if (Key == null) return;
            uint Flag = 0;
            uint i;

            for (i = 0; i < IndexLength / 4; i++)
            {
                uint val = BitConverter.ToUInt32(Buff, (int)i * 4);
                val ^= Key[i & 3];
                val -= 0x4a91c262;
                val = Rol(val, 3);
                val -= Seed;
                byte[] bytes = BitConverter.GetBytes(val);
                Array.Copy(bytes, 0, Buff, (int)i * 4, 4);
                Seed += 0x10fb562a;
                Flag++; Flag &= 3;
            }

            for (i = IndexLength / 4 * 4; i < IndexLength; i++)
            {
                uint Temp = Key[Flag];
                Temp >>= 6;
                Buff[i] ^= (byte)Temp;
                Buff[i] += 0x37;
                Flag++; Flag &= 3;
            }
        }

        private static uint[] GetFileIndexKey2(uint DirKey, cpz_header CPZ_Header)
        {
            uint[] Key = new uint[4];
            Key[0] = DirKey ^ CPZ_Header.Md5Data0;
            Key[2] = DirKey ^ CPZ_Header.Md5Data2;
            Key[1] = (DirKey + 0x11003322) ^ CPZ_Header.Md5Data1;
            DirKey += 0x34216785;
            Key[3] = DirKey ^ CPZ_Header.Md5Data3;
            return Key;
        }

        private static void CPZFileIndexDecrypt1(byte[] Buff, uint Length, uint Key, uint Seed)
        {
            byte[] ByteTable = GetByteTable2(Key, Seed);
            if (ByteTable == null) return;
            for (uint i = 0; i < Length; i++) Buff[i] = ByteTable[Buff[i] ^ 0x7e];
        }

        private static void CPZFileIndexDecrypt2(byte[] FileIndexBuff, uint Length, uint DirKey, cpz_header CPZ_Header)
        {
            uint[] FileIndexKey = GetFileIndexKey2(DirKey, CPZ_Header);
            if (FileIndexKey == null) return;
            uint Seed = 0x2a65cb4f;
            uint Flag = 0;
            uint i;

            for (i = 0; i < Length / 4; i++)
            {
                uint val = BitConverter.ToUInt32(FileIndexBuff, (int)i * 4);
                val ^= FileIndexKey[i & 3];
                val -= Seed;
                val = Rol(val, 2);
                val += 0x37a19e8b;
                byte[] bytes = BitConverter.GetBytes(val);
                Array.Copy(bytes, 0, FileIndexBuff, (int)i * 4, 4);
                Seed -= 0x139fa9b;
                Flag++; Flag &= 3;
            }

            for (i = Length / 4 * 4; i < Length; i++)
            {
                uint Temp = FileIndexKey[Flag];
                Temp >>= 4;
                FileIndexBuff[i] ^= (byte)Temp;
                FileIndexBuff[i] += 3;
                Flag++; Flag &= 3;
            }
        }

        private static void CPZResourceDecrypt(byte[] FileBuff, uint Length, uint IndexKey, uint[] Md5Data, uint Seed)
        {
            uint[] DecryptKey = new uint[24];
            uint Key = Md5Data[1] >> 2;
            byte[] ByteTable = GetByteTable2(Md5Data[3], IndexKey);
            if (ByteTable == null) return;
            byte[] p = new byte[96];
            for (uint i = 0; i < 96; i++)
            {
                p[i] = (byte)(Key ^ ByteTable[ByteString[i] & 0xff]);
            }
            for (uint i = 0; i < 24; i++)
            {
                DecryptKey[i] = BitConverter.ToUInt32(p, (int)i * 4);
                DecryptKey[i] ^= Seed;
            }

            Key = 0x2748c39e;
            uint Flag = 0x0a;
            uint i2;

            for (i2 = 0; i2 < Length / 4; i2++)
            {
                uint val = BitConverter.ToUInt32(FileBuff, (int)i2 * 4);
                uint Temp = DecryptKey[Flag];
                Temp >>= 1;
                Temp ^= DecryptKey[(Key >> 6) & 0xf];
                Temp ^= val;
                Temp -= Seed;
                Temp ^= Md5Data[Key & 3];
                byte[] bytes = BitConverter.GetBytes(Temp);
                Array.Copy(bytes, 0, FileBuff, (int)i2 * 4, 4);
                Key = Key + Seed + Temp;
                Flag++; Flag &= 0xf;
            }

            for (i2 = Length / 4 * 4; i2 < Length; i2++)
            {
                FileBuff[i2] = ByteTable[FileBuff[i2] ^ 0xae];
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
