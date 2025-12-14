using System.Text;

namespace super_toolbox
{
    public class StingTexConverter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.GetFiles(directoryPath, "*.tex", SearchOption.AllDirectories);
            TotalFilesToConvert = filePaths.Length;
            int successCount = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.tex");

                    try
                    {
                        bool conversionSuccess = await ConvertTexFileAsync(filePath, cancellationToken);

                        if (conversionSuccess)
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(filePath)}");
                            OnFileConverted(filePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.tex转换失败");
                            OnConversionFailed($"{fileName}.tex转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.tex处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成，成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成，但未成功转换任何文件");
                }

                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }

        private async Task<bool> ConvertTexFileAsync(string filePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    TEXFile tex = new TEXFile();
                    tex.Load(filePath);
                    tex.SaveSheetImage(Path.ChangeExtension(filePath, ".bmp"));
                    return true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"转换TEX文件失败:{ex.Message}");
                }
            }, cancellationToken);
        }
    }

    public class TEXFrame
    {
        public float Unknown1 { get; set; }
        public float Unknown2 { get; set; }
        public float FrameWidth { get; set; }
        public float FrameHeight { get; set; }
        public float LeftScale { get; set; }
        public float TopScale { get; set; }
        public float RightScale { get; set; }
        public float BottomScale { get; set; }
    }

    public class TEXFile
    {
        public enum Format
        {
            None = 0x0000_0000,
            DXT1 = 0x0000_0001,
            DXT5 = 0x0000_0002,
            Luminance8 = 0x0000_0080,
            Luminance4 = 0x0000_0100,
            Raster = 0x0000_0200,
            DXT12 = 0x0000_1000,
            Large = 0x0000_2000,
            BGRA = 0x0000_4000,
            PNG = 0x0001_0000,
            BGRASpecial = 0x0004_4000,
        }

        public enum LoaderType
        {
            Default = 0x0000_0000,
            PNG = 0x0000_0001,
        }

        public int SheetWidth;
        public int SheetHeight;
        public List<TEXFrame> Frames = new List<TEXFrame>();
        public byte[] SheetData = Array.Empty<byte>();

        public Format TextureFormat = Format.BGRA;
        public LoaderType Loader = LoaderType.Default;
        public bool UsePNG = false;
        public bool UseLZ77 = false;
        public bool UseSmallSig = false;
        public bool Sigless = false;

        public void Load(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                Load(reader);
            }
        }

        public void Load(BinaryReader reader)
        {
            long startPos = reader.BaseStream.Position;

            byte[] header = reader.ReadBytes(4);
            string headerSig = Encoding.ASCII.GetString(header);

            if (headerSig == "ZLIB")
            {
                reader.BaseStream.Position = startPos;
                ProcessZlibFile(reader);
                return;
            }

            reader.BaseStream.Position = startPos;
            ProcessTextureFile(reader);
        }

        private void ProcessZlibFile(BinaryReader reader)
        {
            long startPos = reader.BaseStream.Position;

            string sig = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (sig != "ZLIB") return;

            uint uncompressedSize = reader.ReadUInt32();
            uint compressedSize = reader.ReadUInt32();

            reader.ReadByte();
            reader.ReadByte();

            byte[] compressedData = reader.ReadBytes((int)compressedSize);

            using (var ms = new MemoryStream(compressedData))
            using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var msOut = new MemoryStream())
            {
                ds.CopyTo(msOut);
                byte[] decompressedData = msOut.ToArray();

                using (var ms2 = new MemoryStream(decompressedData))
                using (var reader2 = new BinaryReader(ms2))
                {
                    ProcessTextureFile(reader2);
                }
            }
        }

        private void ProcessTextureFile(BinaryReader reader)
        {
            long startPos = reader.BaseStream.Position;

            byte[] texSig = reader.ReadBytes(7);
            string signature = Encoding.ASCII.GetString(texSig);

            if (signature == "Texture")
            {
                int padding = 0;
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    byte nextByte = reader.ReadByte();
                    if (nextByte != 0x20)
                    {
                        reader.BaseStream.Position--;
                        break;
                    }
                    padding++;
                }

                int totalSigLength = 7 + padding;

                if (totalSigLength >= 0x14)
                {
                    reader.BaseStream.Position = startPos + 0x14;
                }
                else if (totalSigLength >= 0x0C)
                {
                    reader.BaseStream.Position = startPos + 0x0C;
                }
                else if (totalSigLength >= 0x08)
                {
                    reader.BaseStream.Position = startPos + 0x08;
                }
                else
                {
                }

                UseSmallSig = totalSigLength <= 8;

                int textureSectionSize = reader.ReadInt32();
            }
            else
            {
                Sigless = true;
                reader.BaseStream.Position = startPos;
            }

            uint buf = reader.ReadUInt32();
            TextureFormat = (Format)(buf & 0xFFFFFF);
            byte unknown = (byte)(buf >> 24);
            int dataLength;

            if ((unknown & 0b00000001) != 0)
            {
                dataLength = reader.ReadInt32();
                SheetWidth = reader.ReadInt32();
                SheetHeight = reader.ReadInt32();
                Loader = LoaderType.Default;
                UsePNG = false;
            }
            else
            {
                ushort loaderType = reader.ReadUInt16();
                Loader = (LoaderType)loaderType;
                UsePNG = (loaderType & 1) != 0;
                ushort version = reader.ReadUInt16();
                dataLength = reader.ReadInt32();
                SheetWidth = reader.ReadUInt16();
                SheetHeight = reader.ReadUInt16();
            }

            SheetData = reader.ReadBytes(dataLength);

            if (SheetData.Length >= 4)
            {
                string dataSig = Encoding.ASCII.GetString(SheetData, 0, 4);
                UseLZ77 = dataSig == "LZ77";
            }

            if (UseLZ77)
            {
                SheetData = DecompressLZ77(SheetData);
            }

            DecodeImageData();

            bool shouldFixChannels = (TextureFormat == Format.BGRA);

            if (shouldFixChannels)
            {
                if (TextureFormat == Format.DXT1 ||
                    TextureFormat == Format.DXT5 ||
                    TextureFormat == Format.DXT12 ||
                    TextureFormat == Format.Large ||
                    TextureFormat == Format.BGRA ||
                    (uint)TextureFormat == 278528)
                {
                    ConvertBGRAToRGBA();
                }
            }

            if (!Sigless && reader.BaseStream.Position < reader.BaseStream.Length)
            {
                try
                {
                    long currentPos = reader.BaseStream.Position;
                    uint paddingSize = UseSmallSig ? 4u : 8u;
                    while ((currentPos % paddingSize) != 0)
                    {
                        reader.ReadByte();
                        currentPos++;
                    }

                    if (reader.BaseStream.Position + 5 <= reader.BaseStream.Length)
                    {
                        byte[] partsSig = reader.ReadBytes(5);
                        if (Encoding.ASCII.GetString(partsSig) == "Parts")
                        {
                            reader.BaseStream.Position -= 5;
                            ReadParts(reader);
                        }
                    }
                }
                catch { }
            }
        }

        private byte[] DecompressLZ77(byte[] compressed)
        {
            try
            {
                using (var ms = new MemoryStream(compressed))
                using (var reader = new BinaryReader(ms))
                {
                    string sig = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (sig != "LZ77") return compressed;

                    int uncompressedSize = reader.ReadInt32();
                    int step = reader.ReadInt32();
                    int dataOffset = reader.ReadInt32();

                    reader.BaseStream.Position = 0x10;
                    int flagCount = (dataOffset - 0x10);

                    byte[] flags = new byte[flagCount];
                    for (int i = 0; i < flagCount; i++) flags[i] = reader.ReadByte();

                    reader.BaseStream.Position = dataOffset;

                    var output = new List<byte>();
                    int flagIndex = 0;
                    int bitMask = 0x80;
                    byte currentFlag = flags[0];

                    while (output.Count < uncompressedSize && reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        bool isCompressed = (currentFlag & bitMask) != 0;

                        if (isCompressed)
                        {
                            byte backstep = reader.ReadByte();
                            byte amount = reader.ReadByte();

                            int copyLength = amount + 3;
                            int startPos = output.Count - backstep;

                            if (startPos < 0) break;

                            for (int i = 0; i < copyLength; i++)
                            {
                                if (output.Count >= uncompressedSize) break;
                                if (startPos + i >= output.Count) break;
                                output.Add(output[startPos + i]);
                            }
                        }
                        else
                        {
                            if (reader.BaseStream.Position >= reader.BaseStream.Length) break;
                            output.Add(reader.ReadByte());
                        }

                        bitMask >>= 1;
                        if (bitMask == 0)
                        {
                            bitMask = 0x80;
                            flagIndex++;
                            if (flagIndex < flags.Length) currentFlag = flags[flagIndex];
                        }
                    }

                    if (output.Count > uncompressedSize) output = output.GetRange(0, uncompressedSize);
                    return output.ToArray();
                }
            }
            catch
            {
                return compressed;
            }
        }

        private void DecodeImageData()
        {
            if (Loader == LoaderType.PNG || TextureFormat == Format.PNG)
            {
                DecodePNG();
            }
            else if (TextureFormat == Format.DXT1 || TextureFormat == Format.DXT12)
            {
                DecodeDXT1();
            }
            else if (TextureFormat == Format.DXT5 || TextureFormat == Format.Large)
            {
                DecodeDXT5();
            }
            else if (TextureFormat == Format.Luminance8)
            {
                DecodeLuminance8();
            }
            else if (TextureFormat == Format.Luminance4)
            {
                DecodeLuminance4();
            }
            else if (TextureFormat == Format.Raster)
            {
                DecodeRaster();
            }
            else if (TextureFormat == Format.BGRA)
            {
                if (SheetData.Length != SheetWidth * SheetHeight * 4)
                {
                    byte[] newData = new byte[SheetWidth * SheetHeight * 4];
                    Array.Copy(SheetData, 0, newData, 0, Math.Min(SheetData.Length, newData.Length));
                    SheetData = newData;
                }
                ConvertBGRAToRGBA();
            }
        }

        private void ConvertBGRAToRGBA()
        {
            for (int i = 0; i < SheetData.Length; i += 4)
            {
                byte temp = SheetData[i];
                SheetData[i] = SheetData[i + 2];
                SheetData[i + 2] = temp;
            }
        }

        private void DecodePNG()
        {
            using (var ms = new MemoryStream(SheetData))
            using (var pngReader = new BinaryReader(ms))
            {
                byte[] pngHeader = pngReader.ReadBytes(8);
                if (pngHeader[0] == 0x89 && pngHeader[1] == 0x50 && pngHeader[2] == 0x4E && pngHeader[3] == 0x47)
                {
                    byte[] pngBytes = SheetData;
                    SimplePNGDecoder decoder = new SimplePNGDecoder();
                    SheetData = decoder.Decode(pngBytes, out int width, out int height);
                    SheetWidth = width;
                    SheetHeight = height;
                }
            }
        }

        private void DecodeDXT1()
        {
            int width = SheetWidth;
            int height = SheetHeight;
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;

            byte[] output = new byte[width * height * 4];

            using (var ms = new MemoryStream(SheetData))
            using (var blockReader = new BinaryReader(ms))
            {
                for (int y = 0; y < blockCountY; y++)
                {
                    for (int x = 0; x < blockCountX; x++)
                    {
                        ushort color0 = blockReader.ReadUInt16();
                        ushort color1 = blockReader.ReadUInt16();
                        uint bits = blockReader.ReadUInt32();

                        byte[] colors = new byte[4 * 4];

                        byte r0 = (byte)((color0 >> 11) & 0x1F);
                        byte g0 = (byte)((color0 >> 5) & 0x3F);
                        byte b0 = (byte)(color0 & 0x1F);

                        byte r1 = (byte)((color1 >> 11) & 0x1F);
                        byte g1 = (byte)((color1 >> 5) & 0x3F);
                        byte b1 = (byte)(color1 & 0x1F);

                        colors[0] = (byte)((r0 << 3) | (r0 >> 2));
                        colors[1] = (byte)((g0 << 2) | (g0 >> 4));
                        colors[2] = (byte)((b0 << 3) | (b0 >> 2));
                        colors[3] = 255;

                        colors[4] = (byte)((r1 << 3) | (r1 >> 2));
                        colors[5] = (byte)((g1 << 2) | (g1 >> 4));
                        colors[6] = (byte)((b1 << 3) | (b1 >> 2));
                        colors[7] = 255;

                        if (color0 > color1)
                        {
                            colors[8] = (byte)((2 * colors[0] + colors[4]) / 3);
                            colors[9] = (byte)((2 * colors[1] + colors[5]) / 3);
                            colors[10] = (byte)((2 * colors[2] + colors[6]) / 3);
                            colors[11] = 255;

                            colors[12] = (byte)((colors[0] + 2 * colors[4]) / 3);
                            colors[13] = (byte)((colors[1] + 2 * colors[5]) / 3);
                            colors[14] = (byte)((colors[2] + 2 * colors[6]) / 3);
                            colors[15] = 255;
                        }
                        else
                        {
                            colors[8] = (byte)((colors[0] + colors[4]) / 2);
                            colors[9] = (byte)((colors[1] + colors[5]) / 2);
                            colors[10] = (byte)((colors[2] + colors[6]) / 2);
                            colors[11] = 255;

                            colors[12] = 0;
                            colors[13] = 0;
                            colors[14] = 0;
                            colors[15] = 0;
                        }

                        for (int blockY = 0; blockY < 4; blockY++)
                        {
                            for (int blockX = 0; blockX < 4; blockX++)
                            {
                                int pixelX = x * 4 + blockX;
                                int pixelY = y * 4 + blockY;

                                if (pixelX < width && pixelY < height)
                                {
                                    int code = (int)((bits >> (2 * (4 * blockY + blockX))) & 0x03);
                                    int outputIdx = (pixelY * width + pixelX) * 4;

                                    output[outputIdx] = colors[code * 4];
                                    output[outputIdx + 1] = colors[code * 4 + 1];
                                    output[outputIdx + 2] = colors[code * 4 + 2];
                                    output[outputIdx + 3] = colors[code * 4 + 3];
                                }
                            }
                        }
                    }
                }
            }

            SheetData = output;
        }

        private void DecodeDXT5()
        {
            int width = SheetWidth;
            int height = SheetHeight;
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;

            byte[] output = new byte[width * height * 4];

            using (var ms = new MemoryStream(SheetData))
            using (var blockReader = new BinaryReader(ms))
            {
                for (int y = 0; y < blockCountY; y++)
                {
                    for (int x = 0; x < blockCountX; x++)
                    {
                        byte alpha0 = blockReader.ReadByte();
                        byte alpha1 = blockReader.ReadByte();
                        ulong alphaBits = 0;

                        for (int i = 0; i < 6; i++)
                        {
                            alphaBits |= ((ulong)blockReader.ReadByte()) << (8 * i);
                        }

                        ushort color0 = blockReader.ReadUInt16();
                        ushort color1 = blockReader.ReadUInt16();
                        uint colorBits = blockReader.ReadUInt32();

                        byte[] alphas = new byte[8];
                        alphas[0] = alpha0;
                        alphas[1] = alpha1;

                        if (alpha0 > alpha1)
                        {
                            alphas[2] = (byte)((6 * alpha0 + 1 * alpha1) / 7);
                            alphas[3] = (byte)((5 * alpha0 + 2 * alpha1) / 7);
                            alphas[4] = (byte)((4 * alpha0 + 3 * alpha1) / 7);
                            alphas[5] = (byte)((3 * alpha0 + 4 * alpha1) / 7);
                            alphas[6] = (byte)((2 * alpha0 + 5 * alpha1) / 7);
                            alphas[7] = (byte)((1 * alpha0 + 6 * alpha1) / 7);
                        }
                        else
                        {
                            alphas[2] = (byte)((4 * alpha0 + 1 * alpha1) / 5);
                            alphas[3] = (byte)((3 * alpha0 + 2 * alpha1) / 5);
                            alphas[4] = (byte)((2 * alpha0 + 3 * alpha1) / 5);
                            alphas[5] = (byte)((1 * alpha0 + 4 * alpha1) / 5);
                            alphas[6] = 0;
                            alphas[7] = 255;
                        }

                        byte[] colors = new byte[4 * 4];

                        byte r0 = (byte)((color0 >> 11) & 0x1F);
                        byte g0 = (byte)((color0 >> 5) & 0x3F);
                        byte b0 = (byte)(color0 & 0x1F);

                        byte r1 = (byte)((color1 >> 11) & 0x1F);
                        byte g1 = (byte)((color1 >> 5) & 0x3F);
                        byte b1 = (byte)(color1 & 0x1F);

                        colors[0] = (byte)((r0 << 3) | (r0 >> 2));
                        colors[1] = (byte)((g0 << 2) | (g0 >> 4));
                        colors[2] = (byte)((b0 << 3) | (b0 >> 2));
                        colors[3] = 255;

                        colors[4] = (byte)((r1 << 3) | (r1 >> 2));
                        colors[5] = (byte)((g1 << 2) | (g1 >> 4));
                        colors[6] = (byte)((b1 << 3) | (b1 >> 2));
                        colors[7] = 255;

                        colors[8] = (byte)((2 * colors[0] + colors[4]) / 3);
                        colors[9] = (byte)((2 * colors[1] + colors[5]) / 3);
                        colors[10] = (byte)((2 * colors[2] + colors[6]) / 3);
                        colors[11] = 255;

                        colors[12] = (byte)((colors[0] + 2 * colors[4]) / 3);
                        colors[13] = (byte)((colors[1] + 2 * colors[5]) / 3);
                        colors[14] = (byte)((colors[2] + 2 * colors[6]) / 3);
                        colors[15] = 255;

                        for (int blockY = 0; blockY < 4; blockY++)
                        {
                            for (int blockX = 0; blockX < 4; blockX++)
                            {
                                int pixelX = x * 4 + blockX;
                                int pixelY = y * 4 + blockY;

                                if (pixelX < width && pixelY < height)
                                {
                                    int alphaIdx = (int)((alphaBits >> (3 * (4 * blockY + blockX))) & 0x07);
                                    int colorIdx = (int)((colorBits >> (2 * (4 * blockY + blockX))) & 0x03);
                                    int outputIdx = (pixelY * width + pixelX) * 4;

                                    output[outputIdx] = colors[colorIdx * 4];
                                    output[outputIdx + 1] = colors[colorIdx * 4 + 1];
                                    output[outputIdx + 2] = colors[colorIdx * 4 + 2];
                                    output[outputIdx + 3] = alphas[alphaIdx];
                                }
                            }
                        }
                    }
                }
            }

            SheetData = output;
        }

        private void DecodeLuminance8()
        {
            int size = SheetWidth * SheetHeight;
            byte[] output = new byte[size * 4];

            for (int i = 0; i < size; i++)
            {
                byte lum = SheetData[i];
                output[i * 4] = lum;
                output[i * 4 + 1] = lum;
                output[i * 4 + 2] = lum;
                output[i * 4 + 3] = 255;
            }

            SheetData = output;
        }

        private void DecodeLuminance4()
        {
            int size = SheetWidth * SheetHeight;
            byte[] output = new byte[size * 4];

            for (int i = 0; i < size; i++)
            {
                byte packed = SheetData[i / 2];
                byte lum = (byte)((i % 2 == 0) ? ((packed & 0xF0) >> 4) : (packed & 0x0F));
                lum = (byte)(lum * 17);
                output[i * 4] = lum;
                output[i * 4 + 1] = lum;
                output[i * 4 + 2] = lum;
                output[i * 4 + 3] = 255;
            }

            SheetData = output;
        }

        private void DecodeRaster()
        {
            int size = SheetWidth * SheetHeight;
            byte[] output = new byte[size * 4];

            for (int i = 0; i < size; i++)
            {
                byte paletteIdx = SheetData[i];
                output[i * 4] = paletteIdx;
                output[i * 4 + 1] = paletteIdx;
                output[i * 4 + 2] = paletteIdx;
                output[i * 4 + 3] = 255;
            }

            SheetData = output;
        }

        private void ReadParts(BinaryReader reader)
        {
            try
            {
                reader.ReadBytes(5);
                int partsSectionSize = reader.ReadInt32();
                int frameCount = reader.ReadInt32();

                for (int i = 0; i < frameCount; i++)
                {
                    try
                    {
                        Frames.Add(new TEXFrame
                        {
                            Unknown1 = reader.ReadSingle(),
                            Unknown2 = reader.ReadSingle(),
                            FrameWidth = reader.ReadSingle(),
                            FrameHeight = reader.ReadSingle(),
                            LeftScale = reader.ReadSingle(),
                            TopScale = reader.ReadSingle(),
                            RightScale = reader.ReadSingle(),
                            BottomScale = reader.ReadSingle()
                        });
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            catch { }
        }

        public void SaveSheetImage(string path)
        {
            if (SheetData == null || SheetData.Length == 0)
                return;

            int requiredSize = SheetWidth * SheetHeight * 4;
            if (SheetData.Length < requiredSize)
                return;

            SaveAsBMP(path);
        }

        private void SaveAsBMP(string path)
        {
            try
            {
                int width = SheetWidth;
                int height = SheetHeight;

                int rowSize = width * 3;
                int padding = (4 - (rowSize % 4)) % 4;
                int imageSize = (rowSize + padding) * height;
                int fileSize = 54 + imageSize;

                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write((byte)'B');
                    writer.Write((byte)'M');
                    writer.Write(fileSize);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write(54);

                    writer.Write(40);
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write((ushort)1);
                    writer.Write((ushort)24);
                    writer.Write(0);
                    writer.Write(imageSize);
                    writer.Write(2835);
                    writer.Write(2835);
                    writer.Write(0);
                    writer.Write(0);

                    for (int y = height - 1; y >= 0; y--)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int idx = (y * width + x) * 4;
                            if (idx + 3 < SheetData.Length)
                            {
                                byte r = SheetData[idx];
                                byte g = SheetData[idx + 1];
                                byte b = SheetData[idx + 2];

                                writer.Write(b);
                                writer.Write(g);
                                writer.Write(r);
                            }
                            else
                            {
                                writer.Write((byte)0);
                                writer.Write((byte)0);
                                writer.Write((byte)0);
                            }
                        }

                        for (int p = 0; p < padding; p++)
                            writer.Write((byte)0);
                    }
                }
            }
            catch { }
        }
    }

    public class SimplePNGDecoder
    {
        public byte[] Decode(byte[] pngData, out int width, out int height)
        {
            width = 0;
            height = 0;

            using (var ms = new MemoryStream(pngData))
            using (var reader = new BinaryReader(ms))
            {
                byte[] header = reader.ReadBytes(8);
                if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
                    return new byte[0];

                List<byte[]> idatChunks = new List<byte[]>();

                while (ms.Position < ms.Length)
                {
                    uint chunkLength = ReadBigEndianUInt32(reader);
                    string chunkType = Encoding.ASCII.GetString(reader.ReadBytes(4));

                    if (chunkType == "IHDR")
                    {
                        width = ReadBigEndianInt32(reader);
                        height = ReadBigEndianInt32(reader);
                        reader.ReadBytes(5);
                    }
                    else if (chunkType == "IDAT")
                    {
                        byte[] chunkData = reader.ReadBytes((int)chunkLength);
                        idatChunks.Add(chunkData);
                    }
                    else
                    {
                        reader.ReadBytes((int)chunkLength);
                    }

                    reader.ReadBytes(4);
                }

                if (width == 0 || height == 0 || idatChunks.Count == 0)
                    return new byte[0];

                byte[] combinedIdat = CombineArrays(idatChunks);
                byte[] decompressed = DecompressZlib(combinedIdat);

                if (decompressed.Length == 0)
                    return new byte[0];

                int bytesPerPixel = 4;
                int stride = width * bytesPerPixel;
                byte[] output = new byte[height * stride];

                int sourcePos = 0;
                for (int y = 0; y < height; y++)
                {
                    byte filter = decompressed[sourcePos++];

                    for (int x = 0; x < width; x++)
                    {
                        int destPos = (y * width + x) * 4;

                        byte r = decompressed[sourcePos++];
                        byte g = decompressed[sourcePos++];
                        byte b = decompressed[sourcePos++];
                        byte a = decompressed[sourcePos++];

                        if (filter == 1)
                        {
                            if (x > 0)
                            {
                                r = (byte)(r + output[destPos - 4]);
                                g = (byte)(g + output[destPos - 3]);
                                b = (byte)(b + output[destPos - 2]);
                                a = (byte)(a + output[destPos - 1]);
                            }
                        }

                        output[destPos] = r;
                        output[destPos + 1] = g;
                        output[destPos + 2] = b;
                        output[destPos + 3] = a;
                    }
                }

                return output;
            }
        }

        private uint ReadBigEndianUInt32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }

        private int ReadBigEndianInt32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private byte[] CombineArrays(List<byte[]> arrays)
        {
            int totalLength = 0;
            foreach (var array in arrays)
                totalLength += array.Length;

            byte[] result = new byte[totalLength];
            int offset = 0;

            foreach (var array in arrays)
            {
                Array.Copy(array, 0, result, offset, array.Length);
                offset += array.Length;
            }

            return result;
        }

        private byte[] DecompressZlib(byte[] compressed)
        {
            using (var ms = new MemoryStream(compressed, 2, compressed.Length - 2))
            using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                ds.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
