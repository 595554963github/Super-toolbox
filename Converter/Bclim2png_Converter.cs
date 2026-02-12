using System.Drawing.Imaging;

namespace super_toolbox
{
    public class Bclim2png_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private enum CTRFormat
        {
            L8 = 0,
            A8 = 1,
            LA4 = 2,
            LA8 = 3,
            HILO8 = 4,
            RGB565 = 5,
            RGB8 = 6,
            RGBA5551 = 7,
            RGBA4 = 8,
            RGBA8 = 9,
            ETC1 = 10,
            ETC1A4 = 11,
            L4 = 12,
            A4 = 13,
        }

        private class BCLIM
        {
            public byte[] BCLIMImageData { get; set; } = Array.Empty<byte>();
            public IMAG IMAGData { get; set; } = new IMAG();

            public void ReadCLIM(BinaryReader br)
            {
                long FileSizeLength = br.BaseStream.Length;
                br.BaseStream.Position = FileSizeLength;
                br.BaseStream.Seek(-40, SeekOrigin.Current);

                char[] CLIM_Header = br.ReadChars(4);
                if (new string(CLIM_Header) != "CLIM") throw new Exception("BCLIM : Error");

                byte[] BOM = br.ReadBytes(2);
                EndianConvert endianConvert = new EndianConvert(BOM);

                short CLIM_HeaderSize = BitConverter.ToInt16(endianConvert.Convert(br.ReadBytes(2)), 0);
                byte[] Version = endianConvert.Convert(br.ReadBytes(4));
                int FileSize = BitConverter.ToInt32(endianConvert.Convert(br.ReadBytes(4)), 0);
                int UnknownData1 = BitConverter.ToInt32(endianConvert.Convert(br.ReadBytes(4)), 0);

                IMAGData = new IMAG();
                IMAGData.ReadIMAG(br, BOM);

                int ImageDataSize = BitConverter.ToInt32(endianConvert.Convert(br.ReadBytes(4)), 0);

                if (ImageDataSize != 0)
                {
                    br.BaseStream.Seek(0, SeekOrigin.Begin);
                    BCLIMImageData = br.ReadBytes(ImageDataSize);
                }
            }
        }

        private class IMAG
        {
            public short ImageWidth { get; set; }
            public short ImageHeight { get; set; }
            public int ImageFormat { get; set; }

            public CTRFormat CTRFormat
            {
                get { return (CTRFormat)Enum.ToObject(typeof(CTRFormat), ImageFormat); }
                set { ImageFormat = (int)value; }
            }

            public void ReadIMAG(BinaryReader br, byte[] BOM)
            {
                char[] IMAG_Header = br.ReadChars(4);
                if (new string(IMAG_Header) != "imag") throw new Exception("imag : Error");

                EndianConvert endianConvert = new EndianConvert(BOM);
                int HeaderSize = BitConverter.ToInt32(endianConvert.Convert(br.ReadBytes(4)), 0);
                ImageWidth = BitConverter.ToInt16(endianConvert.Convert(br.ReadBytes(2)), 0);
                ImageHeight = BitConverter.ToInt16(endianConvert.Convert(br.ReadBytes(2)), 0);
                ImageFormat = BitConverter.ToInt32(endianConvert.Convert(br.ReadBytes(4)), 0);
            }

            public int GetSize()
            {
                return 4 + 4 + 2 + 2 + 4;
            }
        }

        private class EndianConvert
        {
            public enum Endian
            {
                BigEndian = 65534,
                LittleEndian = 65279
            }

            public byte[] BOM { get; set; }
            public Endian Endians { get; }

            public EndianConvert(byte[] InputBOM)
            {
                BOM = InputBOM;
                Endians = EndianCheck();
            }

            public Endian EndianCheck()
            {
                bool LE = BOM.SequenceEqual(new byte[] { 0xFF, 0xFE });
                bool BE = BOM.SequenceEqual(new byte[] { 0xFE, 0xFF });

                if (LE) return Endian.LittleEndian;
                if (BE) return Endian.BigEndian;
                return Endian.BigEndian;
            }

            public byte[] Convert(byte[] Input)
            {
                if (Endians == Endian.BigEndian)
                    return Input.Reverse().ToArray();
                return Input;
            }
        }

        private static readonly int[] TileOrder =
        {
             0,  1,   4,  5,
             2,  3,   6,  7,
             8,  9,  12, 13,
            10, 11,  14, 15
        };

        private static readonly int[,] ETC1Modifiers =
        {
            { 2, 8 },
            { 5, 17 },
            { 9, 29 },
            { 13, 42 },
            { 18, 60 },
            { 24, 80 },
            { 33, 106 },
            { 47, 183 }
        };

        private class ETC1BitShiftComponent
        {
            public readonly int? AShift;
            public readonly int? RShift;
            public readonly int? GShift;
            public readonly int? BShift;
            public ETC1BitShiftComponent(int? AShift, int? RShift, int? GShift, int? BShift)
            {
                this.AShift = AShift;
                this.RShift = RShift;
                this.GShift = GShift;
                this.BShift = BShift;
            }
        }

        private class ETC1BitSizeComponent
        {
            public readonly int? ASize;
            public readonly int? RSize;
            public readonly int? GSize;
            public readonly int? BSize;
            public ETC1BitSizeComponent(int? ASize, int? RSize, int? GSize, int? BSize)
            {
                this.ASize = ASize;
                this.RSize = RSize;
                this.GSize = GSize;
                this.BSize = BSize;
            }
        }

        private class ETC1ColorFormat
        {
            public readonly ETC1BitShiftComponent ETC1BitShiftComponent;
            public readonly ETC1BitSizeComponent ETC1BitSizeComponent;
            public ETC1ColorFormat(ETC1BitShiftComponent ETC1BitShiftComponent, ETC1BitSizeComponent ETC1BitSizeComponent)
            {
                this.ETC1BitShiftComponent = ETC1BitShiftComponent;
                this.ETC1BitSizeComponent = ETC1BitSizeComponent;
            }

            public static readonly ETC1ColorFormat ARGB8888 = new ETC1ColorFormat(
                new ETC1BitShiftComponent(24, 16, 8, 0),
                new ETC1BitSizeComponent(8, 8, 8, 8));
            public static readonly ETC1ColorFormat RGBA8888 = new ETC1ColorFormat(
                new ETC1BitShiftComponent(null, 24, 15, 8),
                new ETC1BitSizeComponent(8, 8, 8, 8));
            public static readonly ETC1ColorFormat RGB565 = new ETC1ColorFormat(
                new ETC1BitShiftComponent(null, 11, 5, 0),
                new ETC1BitSizeComponent(null, 5, 6, 5));
            public static readonly ETC1ColorFormat RGBA5551 = new ETC1ColorFormat(
                new ETC1BitShiftComponent(null, 11, 6, 1),
                new ETC1BitSizeComponent(1, 5, 5, 5));
            public static readonly ETC1ColorFormat RGBA4444 = new ETC1ColorFormat(
                new ETC1BitShiftComponent(null, 12, 8, 4),
                new ETC1BitSizeComponent(3, 4, 4, 4));
        }

        private static class ETC1ColorFormatConvert
        {
            public static uint ConvertColorFormat(uint InColor, ETC1ColorFormat InputFormat, ETC1ColorFormat OutputFormat)
            {
                if (InputFormat == OutputFormat) return InColor;

                uint A = 255;
                uint R;
                uint G;
                uint B;
                uint mask;

                if (InputFormat.ETC1BitSizeComponent.ASize.HasValue)
                {
                    int aSize = InputFormat.ETC1BitSizeComponent.ASize.Value;
                    mask = ~(0xFFFFFFFFu << aSize);
                    if (InputFormat.ETC1BitShiftComponent.AShift.HasValue)
                    {
                        int aShift = InputFormat.ETC1BitShiftComponent.AShift.Value;
                        A = ((((InColor >> aShift) & mask) * 255u) + mask / 2) / mask;
                    }
                }

                int rSize = InputFormat.ETC1BitSizeComponent.RSize ?? 0;
                mask = ~(0xFFFFFFFFu << rSize);
                int rShift = InputFormat.ETC1BitShiftComponent.RShift ?? 0;
                R = ((((InColor >> rShift) & mask) * 255u) + mask / 2) / mask;

                int gSize = InputFormat.ETC1BitSizeComponent.GSize ?? 0;
                mask = ~(0xFFFFFFFFu << gSize);
                int gShift = InputFormat.ETC1BitShiftComponent.GShift ?? 0;
                G = ((((InColor >> gShift) & mask) * 255u) + mask / 2) / mask;

                int bSize = InputFormat.ETC1BitSizeComponent.BSize ?? 0;
                mask = ~(0xFFFFFFFFu << bSize);
                int bShift = InputFormat.ETC1BitShiftComponent.BShift ?? 0;
                B = ((((InColor >> bShift) & mask) * 255u) + mask / 2) / mask;

                return ToColorFormat(A, R, G, B, OutputFormat);
            }

            public static uint ToColorFormat(uint R, uint G, uint B, ETC1ColorFormat OutputFormat)
            {
                return ToColorFormat(255u, R, G, B, OutputFormat);
            }

            public static uint ToColorFormat(uint A, uint R, uint G, uint B, ETC1ColorFormat OutputFormat)
            {
                uint result = 0;
                uint mask;

                if (OutputFormat.ETC1BitSizeComponent.ASize.HasValue)
                {
                    int aSize = OutputFormat.ETC1BitSizeComponent.ASize.Value;
                    mask = ~(0xFFFFFFFFu << aSize);
                    if (OutputFormat.ETC1BitShiftComponent.AShift.HasValue)
                    {
                        int aShift = OutputFormat.ETC1BitShiftComponent.AShift.Value;
                        result |= ((A * mask + 127u) / 255u) << aShift;
                    }
                }

                int rSize = OutputFormat.ETC1BitSizeComponent.RSize ?? 0;
                mask = ~(0xFFFFFFFFu << rSize);
                int rShift = OutputFormat.ETC1BitShiftComponent.RShift ?? 0;
                result |= ((R * mask + 127u) / 255u) << rShift;

                int gSize = OutputFormat.ETC1BitSizeComponent.GSize ?? 0;
                mask = ~(0xFFFFFFFFu << gSize);
                int gShift = OutputFormat.ETC1BitShiftComponent.GShift ?? 0;
                result |= ((G * mask + 127u) / 255u) << gShift;

                int bSize = OutputFormat.ETC1BitSizeComponent.BSize ?? 0;
                mask = ~(0xFFFFFFFFu << bSize);
                int bShift = OutputFormat.ETC1BitShiftComponent.BShift ?? 0;
                result |= ((B * mask + 127u) / 255u) << bShift;

                return result;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private Bitmap ToBitmap(byte[] Data, int Width, int Height, CTRFormat Format)
        {
            int physicalwidth = Width;
            int physicalheight = Height;
            Width = 1 << (int)Math.Ceiling(Math.Log(Width, 2));
            Height = 1 << (int)Math.Ceiling(Math.Log(Height, 2));

            Bitmap bitm = new Bitmap(physicalwidth, physicalheight);
            BitmapData d = bitm.LockBits(new Rectangle(0, 0, bitm.Width, bitm.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int stride = d.Stride / 4;
            byte[] pixelData = new byte[physicalheight * stride * 4];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, pixelData, 0, pixelData.Length);

            int offs = 0;

            if (Format == CTRFormat.RGBA8)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            uint color = ETC1ColorFormatConvert.ConvertColorFormat(
                                BitConverter.ToUInt32(Data, offs + pos * 4),
                                ETC1ColorFormat.RGBA8888,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64 * 4;
                    }
                }
            }
            else if (Format == CTRFormat.RGBA5551)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            uint color = ETC1ColorFormatConvert.ConvertColorFormat(
                                BitConverter.ToUInt16(Data, offs + pos * 2),
                                ETC1ColorFormat.RGBA5551,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64 * 2;
                    }
                }
            }
            else if (Format == CTRFormat.RGB565)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            uint color = ETC1ColorFormatConvert.ConvertColorFormat(
                                BitConverter.ToUInt16(Data, offs + pos * 2),
                                ETC1ColorFormat.RGB565,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64 * 2;
                    }
                }
            }
            else if (Format == CTRFormat.RGBA4)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            uint color = ETC1ColorFormatConvert.ConvertColorFormat(
                                BitConverter.ToUInt16(Data, offs + pos * 2),
                                ETC1ColorFormat.RGBA4444,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64 * 2;
                    }
                }
            }
            else if (Format == CTRFormat.LA8)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            byte l = Data[offs + pos * 2];
                            byte a = Data[offs + pos * 2 + 1];
                            uint color = ETC1ColorFormatConvert.ToColorFormat(
                                a,
                                l,
                                l,
                                l,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64 * 2;
                    }
                }
            }
            else if (Format == CTRFormat.HILO8)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            byte hi = Data[offs + pos * 2];
                            byte lo = Data[offs + pos * 2 + 1];
                            uint color = ETC1ColorFormatConvert.ToColorFormat(
                                255,
                                hi,
                                lo,
                                255,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64 * 2;
                    }
                }
            }
            else if (Format == CTRFormat.L8)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            uint color = ETC1ColorFormatConvert.ToColorFormat(
                                Data[offs + pos],
                                Data[offs + pos],
                                Data[offs + pos],
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64;
                    }
                }
            }
            else if (Format == CTRFormat.A8)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            uint color = ETC1ColorFormatConvert.ToColorFormat(
                                Data[offs + pos],
                                255,
                                255,
                                255,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64;
                    }
                }
            }
            else if (Format == CTRFormat.LA4)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            byte l = (byte)((Data[offs + pos] >> 4) * 0x11);
                            byte a = (byte)((Data[offs + pos] & 0xF) * 0x11);
                            uint color = ETC1ColorFormatConvert.ToColorFormat(
                                a,
                                l,
                                l,
                                l,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64;
                    }
                }
            }
            else if (Format == CTRFormat.L4)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            int shift = (pos & 1) * 4;
                            byte l = (byte)(((Data[offs + pos / 2] >> shift) & 0xF) * 0x11);
                            uint color = ETC1ColorFormatConvert.ToColorFormat(
                                l,
                                l,
                                l,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64 / 2;
                    }
                }
            }
            else if (Format == CTRFormat.A4)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            int x2 = i % 8;
                            if (x + x2 >= physicalwidth) continue;
                            int y2 = i / 8;
                            if (y + y2 >= physicalheight) continue;
                            int pos = TileOrder[x2 % 4 + y2 % 4 * 4] + 16 * (x2 / 4) + 32 * (y2 / 4);
                            int pixelIndex = ((y + y2) * stride + x + x2) * 4;
                            int shift = (pos & 1) * 4;
                            byte a = (byte)(((Data[offs + pos / 2] >> shift) & 0xF) * 0x11);
                            uint color = ETC1ColorFormatConvert.ToColorFormat(
                                a,
                                255,
                                255,
                                255,
                                ETC1ColorFormat.ARGB8888);
                            byte[] colorBytes = BitConverter.GetBytes(color);
                            Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                        }
                        offs += 64 / 2;
                    }
                }
            }
            else if (Format == CTRFormat.ETC1)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 8; i += 4)
                        {
                            for (int j = 0; j < 8; j += 4)
                            {
                                ulong alpha = 0xFFFFFFFFFFFFFFFF;
                                ulong data = BitConverter.ToUInt64(Data, offs);
                                bool diffbit = ((data >> 33) & 1) == 1;
                                bool flipbit = ((data >> 32) & 1) == 1;
                                int r1, r2, g1, g2, b1, b2;

                                if (diffbit)
                                {
                                    int r = (int)((data >> 59) & 0x1F);
                                    int g = (int)((data >> 51) & 0x1F);
                                    int b = (int)((data >> 43) & 0x1F);
                                    r1 = (r << 3) | ((r & 0x1C) >> 2);
                                    g1 = (g << 3) | ((g & 0x1C) >> 2);
                                    b1 = (b << 3) | ((b & 0x1C) >> 2);
                                    r += (int)((data >> 56) & 0x7) << 29 >> 29;
                                    g += (int)((data >> 48) & 0x7) << 29 >> 29;
                                    b += (int)((data >> 40) & 0x7) << 29 >> 29;
                                    r2 = (r << 3) | ((r & 0x1C) >> 2);
                                    g2 = (g << 3) | ((g & 0x1C) >> 2);
                                    b2 = (b << 3) | ((b & 0x1C) >> 2);
                                }
                                else
                                {
                                    r1 = (int)((data >> 60) & 0xF) * 0x11;
                                    g1 = (int)((data >> 52) & 0xF) * 0x11;
                                    b1 = (int)((data >> 44) & 0xF) * 0x11;
                                    r2 = (int)((data >> 56) & 0xF) * 0x11;
                                    g2 = (int)((data >> 48) & 0xF) * 0x11;
                                    b2 = (int)((data >> 40) & 0xF) * 0x11;
                                }

                                int Table1 = (int)((data >> 37) & 0x7);
                                int Table2 = (int)((data >> 34) & 0x7);

                                for (int y3 = 0; y3 < 4; y3++)
                                {
                                    for (int x3 = 0; x3 < 4; x3++)
                                    {
                                        if (x + j + x3 >= physicalwidth) continue;
                                        if (y + i + y3 >= physicalheight) continue;

                                        int val = (int)((data >> (x3 * 4 + y3)) & 0x1);
                                        bool neg = ((data >> (x3 * 4 + y3 + 16)) & 0x1) == 1;
                                        uint c;

                                        if ((flipbit && y3 < 2) || (!flipbit && x3 < 2))
                                        {
                                            int add = ETC1Modifiers[Table1, val] * (neg ? -1 : 1);
                                            c = ETC1ColorFormatConvert.ToColorFormat(
                                                (byte)(((alpha >> ((x3 * 4 + y3) * 4)) & 0xF) * 0x11),
                                                (byte)ColorClamp(r1 + add),
                                                (byte)ColorClamp(g1 + add),
                                                (byte)ColorClamp(b1 + add),
                                                ETC1ColorFormat.ARGB8888);
                                        }
                                        else
                                        {
                                            int add = ETC1Modifiers[Table2, val] * (neg ? -1 : 1);
                                            c = ETC1ColorFormatConvert.ToColorFormat(
                                                (byte)(((alpha >> ((x3 * 4 + y3) * 4)) & 0xF) * 0x11),
                                                (byte)ColorClamp(r2 + add),
                                                (byte)ColorClamp(g2 + add),
                                                (byte)ColorClamp(b2 + add),
                                                ETC1ColorFormat.ARGB8888);
                                        }
                                        int pixelIndex = ((y + i + y3) * stride + x + j + x3) * 4;
                                        byte[] colorBytes = BitConverter.GetBytes(c);
                                        Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                                    }
                                }
                                offs += 8;
                            }
                        }
                    }
                }
            }
            else if (Format == CTRFormat.ETC1A4)
            {
                for (int y = 0; y < Height; y += 8)
                {
                    for (int x = 0; x < Width; x += 8)
                    {
                        for (int i = 0; i < 8; i += 4)
                        {
                            for (int j = 0; j < 8; j += 4)
                            {
                                ulong alpha = BitConverter.ToUInt64(Data, offs);
                                offs += 8;
                                ulong data = BitConverter.ToUInt64(Data, offs);
                                bool diffbit = ((data >> 33) & 1) == 1;
                                bool flipbit = ((data >> 32) & 1) == 1;
                                int r1, r2, g1, g2, b1, b2;

                                if (diffbit)
                                {
                                    int r = (int)((data >> 59) & 0x1F);
                                    int g = (int)((data >> 51) & 0x1F);
                                    int b = (int)((data >> 43) & 0x1F);
                                    r1 = (r << 3) | ((r & 0x1C) >> 2);
                                    g1 = (g << 3) | ((g & 0x1C) >> 2);
                                    b1 = (b << 3) | ((b & 0x1C) >> 2);
                                    r += (int)((data >> 56) & 0x7) << 29 >> 29;
                                    g += (int)((data >> 48) & 0x7) << 29 >> 29;
                                    b += (int)((data >> 40) & 0x7) << 29 >> 29;
                                    r2 = (r << 3) | ((r & 0x1C) >> 2);
                                    g2 = (g << 3) | ((g & 0x1C) >> 2);
                                    b2 = (b << 3) | ((b & 0x1C) >> 2);
                                }
                                else
                                {
                                    r1 = (int)((data >> 60) & 0xF) * 0x11;
                                    g1 = (int)((data >> 52) & 0xF) * 0x11;
                                    b1 = (int)((data >> 44) & 0xF) * 0x11;
                                    r2 = (int)((data >> 56) & 0xF) * 0x11;
                                    g2 = (int)((data >> 48) & 0xF) * 0x11;
                                    b2 = (int)((data >> 40) & 0xF) * 0x11;
                                }

                                int Table1 = (int)((data >> 37) & 0x7);
                                int Table2 = (int)((data >> 34) & 0x7);

                                for (int y3 = 0; y3 < 4; y3++)
                                {
                                    for (int x3 = 0; x3 < 4; x3++)
                                    {
                                        if (x + j + x3 >= physicalwidth) continue;
                                        if (y + i + y3 >= physicalheight) continue;

                                        int val = (int)((data >> (x3 * 4 + y3)) & 0x1);
                                        bool neg = ((data >> (x3 * 4 + y3 + 16)) & 0x1) == 1;
                                        uint c;

                                        if ((flipbit && y3 < 2) || (!flipbit && x3 < 2))
                                        {
                                            int add = ETC1Modifiers[Table1, val] * (neg ? -1 : 1);
                                            c = ETC1ColorFormatConvert.ToColorFormat(
                                                (byte)(((alpha >> ((x3 * 4 + y3) * 4)) & 0xF) * 0x11),
                                                (byte)ColorClamp(r1 + add),
                                                (byte)ColorClamp(g1 + add),
                                                (byte)ColorClamp(b1 + add),
                                                ETC1ColorFormat.ARGB8888);
                                        }
                                        else
                                        {
                                            int add = ETC1Modifiers[Table2, val] * (neg ? -1 : 1);
                                            c = ETC1ColorFormatConvert.ToColorFormat(
                                                (byte)(((alpha >> ((x3 * 4 + y3) * 4)) & 0xF) * 0x11),
                                                (byte)ColorClamp(r2 + add),
                                                (byte)ColorClamp(g2 + add),
                                                (byte)ColorClamp(b2 + add),
                                                ETC1ColorFormat.ARGB8888);
                                        }
                                        int pixelIndex = ((y + i + y3) * stride + x + j + x3) * 4;
                                        byte[] colorBytes = BitConverter.GetBytes(c);
                                        Array.Copy(colorBytes, 0, pixelData, pixelIndex, 4);
                                    }
                                }
                                offs += 8;
                            }
                        }
                    }
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, d.Scan0, pixelData.Length);
            bitm.UnlockBits(d);
            return bitm;
        }

        private int ColorClamp(int Color)
        {
            if (Color > 255) Color = 255;
            if (Color < 0) Color = 0;
            return Color;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> convertedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");

            var bclimFiles = Directory.EnumerateFiles(directoryPath, "*.bclim", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = bclimFiles.Count;
            int successCount = 0;

            foreach (var bclimFilePath in bclimFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(bclimFilePath)}");

                try
                {
                    Bitmap? bitmap = null;
                    try
                    {
                        using (FileStream fs = new FileStream(bclimFilePath, FileMode.Open, FileAccess.Read))
                        using (BinaryReader br = new BinaryReader(fs))
                        {
                            BCLIM bclim = new BCLIM();
                            bclim.ReadCLIM(br);
                            bitmap = ToBitmap(bclim.BCLIMImageData, bclim.IMAGData.ImageWidth, bclim.IMAGData.ImageHeight, bclim.IMAGData.CTRFormat);
                        }

                        string pngPath = Path.ChangeExtension(bclimFilePath, ".png");
                        bitmap.Save(pngPath, ImageFormat.Png);
                        bitmap.Dispose();
                        bitmap = null;

                        successCount++;
                        convertedFiles.Add(pngPath);
                        ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngPath)}");
                        OnFileConverted(pngPath);
                    }
                    finally
                    {
                        if (bitmap != null)
                            bitmap.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    ConversionError?.Invoke(this, $"转换异常:{Path.GetFileName(bclimFilePath)} - {ex.Message}");
                    OnConversionFailed($"{Path.GetFileName(bclimFilePath)} 处理错误:{ex.Message}");
                }
            }

            if (successCount > 0)
            {
                ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                OnConversionCompleted();
            }
            else
            {
                ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
                OnConversionCompleted();
            }
        }
    }
}
