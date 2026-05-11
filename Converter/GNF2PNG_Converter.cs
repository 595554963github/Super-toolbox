using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class GNF2PNG_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> convertedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var gnfFiles = Directory.EnumerateFiles(directoryPath, "*.gnf", SearchOption.AllDirectories).ToArray();
            TotalFilesToConvert = gnfFiles.Length;

            int successCount = 0;

            try
            {
                foreach (var gnfFilePath in gnfFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(gnfFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(gnfFilePath);
                    string fileDirectory = Path.GetDirectoryName(gnfFilePath) ?? string.Empty;

                    fileName = fileName.Replace(".png", "", StringComparison.OrdinalIgnoreCase);

                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    try
                    {
                        bool conversionSuccess = await ConvertGnfToPng(gnfFilePath, pngFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(pngFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(pngFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngFilePath)}");
                            OnFileConverted(pngFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.gnf转换失败");
                            OnConversionFailed($"{fileName}.gnf转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.gnf处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
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

        private async Task<bool> ConvertGnfToPng(string gnfFilePath, string pngFilePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string? outputDir = Path.GetDirectoryName(pngFilePath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                        Directory.CreateDirectory(outputDir);

                    using (var stream = File.OpenRead(gnfFilePath))
                    {
                        var gnf = new GNF();
                        gnf.Open(stream);
                        using (var bitmap = gnf.GetBitmap())
                        {
                            bitmap.Save(pngFilePath, ImageFormat.Png);
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConversionError?.Invoke(this, $"转换失败:{ex.Message}");
                    if (File.Exists(pngFilePath))
                    {
                        try { File.Delete(pngFilePath); } catch { }
                    }
                    return false;
                }
            }, cancellationToken);
        }

        private enum GnfDataFormat : byte
        {
            FormatInvalid = 0x0,
            Format8 = 0x1,
            Format16 = 0x2,
            Format8_8 = 0x3,
            Format32 = 0x4,
            Format16_16 = 0x5,
            Format10_11_11 = 0x6,
            Format11_11_10 = 0x7,
            Format10_10_10_2 = 0x8,
            Format2_10_10_10 = 0x9,
            Format8_8_8_8 = 0xa,
            Format32_32 = 0xb,
            Format16_16_16_16 = 0xc,
            Format32_32_32 = 0xd,
            Format32_32_32_32 = 0xe,
            FormatReserved_15 = 0xf,
            Format5_6_5 = 0x10,
            Format1_5_5_5 = 0x11,
            Format5_5_5_1 = 0x12,
            Format4_4_4_4 = 0x13,
            Format8_24 = 0x14,
            Format24_8 = 0x15,
            FormatX24_8_32 = 0x16,
            FormatReserved_23 = 0x17,
            FormatReserved_24 = 0x18,
            FormatReserved_25 = 0x19,
            FormatReserved_26 = 0x1a,
            FormatReserved_27 = 0x1b,
            FormatReserved_28 = 0x1c,
            FormatReserved_29 = 0x1d,
            FormatReserved_30 = 0x1e,
            FormatReserved_31 = 0x1f,
            FormatGB_GR = 0x20,
            FormatBG_RG = 0x21,
            Format5_9_9_9 = 0x22,
            FormatBC1 = 0x23,
            FormatBC2 = 0x24,
            FormatBC3 = 0x25,
            FormatBC4 = 0x26,
            FormatBC5 = 0x27,
            FormatBC6 = 0x28,
            FormatBC7 = 0x29,
            FormatReserved_42 = 0x2a,
            FormatReserved_43 = 0x2b,
            FormatFMask8_S2_F1 = 0x2c,
            FormatFMask8_S4_F1 = 0x2d,
            FormatFMask8_S8_F1 = 0x2e,
            FormatFMask8_S2_F2 = 0x2f,
            FormatFMask8_S4_F2 = 0x30,
            FormatFMask8_S4_F4 = 0x31,
            FormatFMask16_S16_F1 = 0x32,
            FormatFMask16_S8_F2 = 0x33,
            FormatFMask32_S16_F2 = 0x34,
            FormatFMask32_S8_F4 = 0x35,
            FormatFMask32_S8_F8 = 0x36,
            FormatFMask64_S16_F4 = 0x37,
            FormatFMask64_S16_F8 = 0x38,
            Format4_4 = 0x39,
            Format6_5_5 = 0x3a,
            Format1 = 0x3b,
            Format1_Reversed = 0x3c,
            Format32_AS_8 = 0x3d,
            Format32_AS_8_8 = 0x3e,
            Format32_AS_32_32_32_32 = 0x3f
        }

        private enum GnfNumFormat
        {
            FormatUNorm = 0x0,
            FormatSNorm = 0x1,
            FormatUScaled = 0x2,
            FormatSScaled = 0x3,
            FormatUInt = 0x4,
            FormatSInt = 0x5,
            FormatSNorm_OGL = 0x6,
            FormatFloat = 0x7,
            FormatReserved_8 = 0x8,
            FormatSRGB = 0x9,
            FormatUBNorm = 0xa,
            FormatUBNorm_OGL = 0xb,
            FormatUBInt = 0xc,
            FormatUBScaled = 0xd,
            FormatReserved_14 = 0xe,
            FormatReserved_15 = 0xf
        }

        private enum GnfSqSel : byte
        {
            Sel0 = 0x0,
            Sel1 = 0x1,
            SelReserved_0 = 0x2,
            SelReserved_1 = 0x3,
            SelX = 0x4,
            SelY = 0x5,
            SelZ = 0x6,
            SelW = 0x7
        }

        private enum PixelDataFormat : ulong
        {
            Undefined = 0x00000000,
            Bpp32 = ((ulong)5 << 0),
            MaskBpp = ((((ulong)1 << 3) - 1) << 0),
            ChannelsAbgr = ((ulong)6 << 3),
            ChannelsArgb = ((ulong)5 << 3),
            ChannelsRgb = ((ulong)1 << 3),
            ChannelsRgba = ((ulong)3 << 3),
            MaskChannels = ((((ulong)1 << 5) - 1) << 3),
            RedBits8 = ((ulong)3 << 8),
            GreenBits8 = ((ulong)4 << 15),
            BlueBits8 = ((ulong)3 << 22),
            AlphaBits8 = ((ulong)3 << 29),
            MaskSpecial = ((((ulong)1 << 5) - 1) << 36),
            SpecialFormatDXT1 = ((ulong)3 << 36),
            SpecialFormatDXT3 = ((ulong)5 << 36),
            SpecialFormatDXT5 = ((ulong)7 << 36),
            SpecialFormatRGTC1 = ((ulong)11 << 36),
            SpecialFormatRGTC1_Signed = ((ulong)12 << 36),
            SpecialFormatRGTC2 = ((ulong)13 << 36),
            SpecialFormatRGTC2_Signed = ((ulong)14 << 36),
            SpecialFormatBPTC = ((ulong)15 << 36),
            MaskPixelOrdering = ((((ulong)1 << 8) - 1) << 41),
            PixelOrderingTiled3DS = ((ulong)1 << 42),
            FormatDXT1Rgba = (SpecialFormatDXT1 | Bpp32 | ChannelsRgba),
            FormatDXT3 = (SpecialFormatDXT3 | Bpp32 | ChannelsRgba),
            FormatDXT5 = (SpecialFormatDXT5 | Bpp32 | ChannelsRgba),
            FormatRGTC1 = (SpecialFormatRGTC1 | Bpp32),
            FormatRGTC1_Signed = (SpecialFormatRGTC1_Signed | Bpp32),
            FormatRGTC2 = (SpecialFormatRGTC2 | Bpp32),
            FormatRGTC2_Signed = (SpecialFormatRGTC2_Signed | Bpp32),
            FormatBPTC = (SpecialFormatBPTC | Bpp32),
            FormatArgb8888 = (Bpp32 | ChannelsArgb | RedBits8 | GreenBits8 | BlueBits8 | AlphaBits8),
            FormatAbgr8888 = (Bpp32 | ChannelsAbgr | RedBits8 | GreenBits8 | BlueBits8 | AlphaBits8)
        }

        private class GNF
        {
            public string? MagicNumber { get; private set; }
            public uint FileSize { get; private set; }
            public uint ImageInformation1 { get; private set; }
            public uint ImageInformation2 { get; private set; }
            public uint ImageInformation3 { get; private set; }
            public uint ImageInformation4 { get; private set; }
            public uint DataSize { get; private set; }
            public byte[]? PixelData { get; private set; }

            private GnfDataFormat dataFormat;
            private GnfNumFormat numFormat;
            private int width, height, pitch;
            private GnfSqSel destX, destY, destZ, destW;

            public void Open(Stream stream)
            {
                using (var reader = new BinaryReader(stream))
                {
                    MagicNumber = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4), 0, 4);
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    FileSize = reader.ReadUInt32();
                    reader.ReadUInt32();
                    ImageInformation1 = reader.ReadUInt32();
                    ImageInformation2 = reader.ReadUInt32();
                    ImageInformation3 = reader.ReadUInt32();
                    ImageInformation4 = reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    DataSize = reader.ReadUInt32();
                    reader.ReadBytes(4);
                    for (int i = 0; i < 0x33; i++) reader.ReadUInt32();
                    PixelData = reader.ReadBytes((int)DataSize);
                }

                dataFormat = (GnfDataFormat)ExtractData(ImageInformation1, 20, 25);
                numFormat = (GnfNumFormat)ExtractData(ImageInformation1, 26, 29);
                width = (int)(ExtractData(ImageInformation2, 0, 13) + 1);
                height = (int)(ExtractData(ImageInformation2, 14, 27) + 1);
                pitch = (int)(ExtractData(ImageInformation4, 13, 26) + 1);
                destX = (GnfSqSel)ExtractData(ImageInformation3, 0, 2);
                destY = (GnfSqSel)ExtractData(ImageInformation3, 3, 5);
                destZ = (GnfSqSel)ExtractData(ImageInformation3, 6, 8);
                destW = (GnfSqSel)ExtractData(ImageInformation3, 9, 11);
            }

            private uint ExtractData(uint val, int first, int last)
            {
                uint mask = (((uint)(1 << ((last + 1) - first)) - 1) << first);
                return ((val & mask) >> first);
            }

            public Bitmap GetBitmap()
            {
                bool isAbgr = (destX == GnfSqSel.SelX && destY == GnfSqSel.SelY && destZ == GnfSqSel.SelZ && destW == GnfSqSel.SelW);

                int height_fix = height / 32;
                int height_rest = height % 32;
                height_fix += height_rest != 0 ? 1 : 0;
                height_fix *= 32;

                int width_fix = pitch / 32;
                int width_rest = pitch % 32;
                width_fix += width_rest != 0 ? 1 : 0;
                width_fix *= 32;

                PixelDataFormat inputPixelFormat = PixelDataFormat.Undefined;
                switch (dataFormat)
                {
                    case GnfDataFormat.Format8_8_8_8:
                        inputPixelFormat = isAbgr ? PixelDataFormat.FormatAbgr8888 : PixelDataFormat.FormatArgb8888;
                        break;
                    case GnfDataFormat.FormatBC1:
                        inputPixelFormat = PixelDataFormat.FormatDXT1Rgba;
                        break;
                    case GnfDataFormat.FormatBC2:
                        inputPixelFormat = PixelDataFormat.FormatDXT3;
                        break;
                    case GnfDataFormat.FormatBC3:
                        inputPixelFormat = PixelDataFormat.FormatDXT5;
                        break;
                    case GnfDataFormat.FormatBC4:
                        inputPixelFormat = (numFormat == GnfNumFormat.FormatSNorm ? PixelDataFormat.FormatRGTC1_Signed : PixelDataFormat.FormatRGTC1);
                        break;
                    case GnfDataFormat.FormatBC5:
                        inputPixelFormat = (numFormat == GnfNumFormat.FormatSNorm ? PixelDataFormat.FormatRGTC2_Signed : PixelDataFormat.FormatRGTC2);
                        break;
                    case GnfDataFormat.FormatBC7:
                        inputPixelFormat = PixelDataFormat.FormatBPTC;
                        break;
                    default:
                        throw new NotImplementedException($"未实现的GNF数据格式{dataFormat}");
                }

                inputPixelFormat |= PixelDataFormat.PixelOrderingTiled3DS;

                return ImageBinary.GetBitmap(width, height, width_fix, height_fix, inputPixelFormat, PixelData!);
            }
        }

        private static class ImageBinary
        {
            private delegate void PixelOrderingDelegate(int origX, int origY, int width, int height, PixelDataFormat pixelFormat, out int transformedX, out int transformedY);

            private static readonly int[] pixelOrderingTiled3DS =
            {
                 0,  1,  8,  9,  2,  3, 10, 11,
                16, 17, 24, 25, 18, 19, 26, 27,
                 4,  5, 12, 13,  6,  7, 14, 15,
                20, 21, 28, 29, 22, 23, 30, 31,
                32, 33, 40, 41, 34, 35, 42, 43,
                48, 49, 56, 57, 50, 51, 58, 59,
                36, 37, 44, 45, 38, 39, 46, 47,
                52, 53, 60, 61, 54, 55, 62, 63
            };

            public static Bitmap GetBitmap(int virtualWidth, int virtualHeight, int physicalWidth, int physicalHeight, PixelDataFormat inputPixelFormat, byte[] inputPixels)
            {
                byte[] pixelData = ConvertPixelDataToArgb8888(inputPixels, inputPixelFormat, physicalWidth, physicalHeight);

                Bitmap image = new Bitmap(physicalWidth, physicalHeight, PixelFormat.Format32bppArgb);
                BitmapData bmpData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, image.PixelFormat);

                byte[] pixelsForBmp = new byte[bmpData.Height * bmpData.Stride];
                int copySize = bmpData.Width * 4;

                for (int y = 0; y < bmpData.Height; y++)
                {
                    int srcOffset = y * physicalWidth * 4;
                    int dstOffset = y * bmpData.Stride;
                    if (srcOffset >= pixelData.Length || dstOffset >= pixelsForBmp.Length) continue;
                    Buffer.BlockCopy(pixelData, srcOffset, pixelsForBmp, dstOffset, Math.Min(copySize, pixelData.Length - srcOffset));
                }

                Marshal.Copy(pixelsForBmp, 0, bmpData.Scan0, pixelsForBmp.Length);
                image.UnlockBits(bmpData);

                return image.Clone(new Rectangle(0, 0, virtualWidth, virtualHeight), image.PixelFormat);
            }

            private static byte[] ConvertPixelDataToArgb8888(byte[] inputData, PixelDataFormat inputPixelFormat, int physicalWidth, int physicalHeight)
            {
                PixelDataFormat specialFormat = (inputPixelFormat & PixelDataFormat.MaskSpecial);
                if (specialFormat != PixelDataFormat.Undefined)
                    return ConvertSpecialInputToArgb8888(inputData, inputPixelFormat, physicalWidth, physicalHeight);
                else
                    return ConvertNormalInputToArgb8888(inputData, inputPixelFormat, physicalWidth, physicalHeight);
            }

            private static byte[] ConvertSpecialInputToArgb8888(byte[] inputData, PixelDataFormat inputPixelFormat, int width, int height)
            {
                return DXTxRGTC.Decompress(inputData, width, height, inputPixelFormat);
            }

            private static byte[] ConvertNormalInputToArgb8888(byte[] inputData, PixelDataFormat inputPixelFormat, int physicalWidth, int physicalHeight)
            {
                byte[] dataArgb8888 = new byte[physicalWidth * physicalHeight * 4];
                bool isAbgr = (inputPixelFormat & PixelDataFormat.MaskChannels) == PixelDataFormat.ChannelsAbgr;

                PixelOrderingDelegate pixelOrderingFunc = GetPixelOrderingFunction(inputPixelFormat);

                using (var reader = new BinaryReader(new MemoryStream(inputData)))
                {
                    for (int i = 0, x = 0, y = 0; i < inputData.Length - (inputData.Length % 4); i += 4)
                    {
                        uint rawData = reader.ReadUInt32();
                        uint alpha = (rawData >> 24) & 0xFF;
                        uint blue = (rawData >> 16) & 0xFF;
                        uint green = (rawData >> 8) & 0xFF;
                        uint red = rawData & 0xFF;

                        int tx, ty;
                        pixelOrderingFunc(x, y, physicalWidth, physicalHeight, inputPixelFormat, out tx, out ty);

                        if (tx < physicalWidth && ty < physicalHeight)
                        {
                            int pixelOffset = ((ty * physicalWidth) + tx) * 4;
                            if (isAbgr)
                            {
                                dataArgb8888[pixelOffset + 3] = (byte)(alpha & 0xFF);
                                dataArgb8888[pixelOffset + 2] = (byte)(red & 0xFF);
                                dataArgb8888[pixelOffset + 1] = (byte)(green & 0xFF);
                                dataArgb8888[pixelOffset + 0] = (byte)(blue & 0xFF);
                            }
                            else
                            {
                                dataArgb8888[pixelOffset + 3] = (byte)(alpha & 0xFF);
                                dataArgb8888[pixelOffset + 2] = (byte)(blue & 0xFF);
                                dataArgb8888[pixelOffset + 1] = (byte)(green & 0xFF);
                                dataArgb8888[pixelOffset + 0] = (byte)(red & 0xFF);
                            }
                        }

                        x++;
                        if (x == physicalWidth) { x = 0; y++; }
                    }
                }

                return dataArgb8888;
            }

            private static PixelOrderingDelegate GetPixelOrderingFunction(PixelDataFormat inputPixelFormat)
            {
                PixelDataFormat pixelOrdering = (inputPixelFormat & PixelDataFormat.MaskPixelOrdering);
                switch (pixelOrdering)
                {
                    case PixelDataFormat.PixelOrderingTiled3DS:
                        return GetPixelCoordinates3DS;
                    default:
                        return (int x, int y, int w, int h, PixelDataFormat pf, out int tx, out int ty) => { tx = x; ty = y; };
                }
            }

            private static void GetPixelCoordinates3DS(int origX, int origY, int width, int height, PixelDataFormat inputPixelFormat, out int transformedX, out int transformedY)
            {
                GetPixelCoordinatesTiledEx(origX, origY, width, height, 8, 8, pixelOrderingTiled3DS, out transformedX, out transformedY);
            }

            private static void GetPixelCoordinatesTiledEx(int origX, int origY, int width, int height, int tileWidth, int tileHeight, int[] pixelOrdering, out int transformedX, out int transformedY)
            {
                if (width == 0) width = tileWidth;
                if (height == 0) height = tileHeight;

                int tileSize = (tileWidth * tileHeight);
                int globalPixel = ((origY * width) + origX);
                int globalX = ((globalPixel / tileSize) * tileWidth);
                int globalY = ((globalX / width) * tileHeight);
                globalX %= width;

                int inTileX = (globalPixel % tileWidth);
                int inTileY = ((globalPixel / tileWidth) % tileHeight);
                int inTilePixel = ((inTileY * tileHeight) + inTileX);

                if (pixelOrdering != null && tileSize <= pixelOrdering.Length)
                {
                    inTileX = (pixelOrdering[inTilePixel] % 8);
                    inTileY = (pixelOrdering[inTilePixel] / 8);
                }

                transformedX = (globalX + inTileX);
                transformedY = (globalY + inTileY);
            }
        }

        private static class DXTxRGTC
        {
            private enum BlockLayout { Normal, PSP }
            private enum Signedness { Unsigned, Signed }

            public static byte[] Decompress(byte[] inputData, int width, int height, PixelDataFormat inputFormat)
            {
                byte[] outPixels = new byte[width * height * 4];

                using (var reader = new BinaryReader(new MemoryStream(inputData)))
                {
                    PixelDataFormat specialFormat = (inputFormat & PixelDataFormat.MaskSpecial);

                    for (int y = 0; y < height; y += 4)
                    {
                        for (int x = 0; x < width; x += 4)
                        {
                            if (reader.BaseStream.Position < reader.BaseStream.Length)
                            {
                                byte[] decompressedBlock;
                                switch (specialFormat)
                                {
                                    case PixelDataFormat.SpecialFormatDXT1:
                                        decompressedBlock = DecodeDXT1Block(reader, BlockLayout.Normal, (inputFormat & PixelDataFormat.MaskChannels) != PixelDataFormat.ChannelsRgb);
                                        break;
                                    case PixelDataFormat.SpecialFormatDXT3:
                                        decompressedBlock = DecodeDXT3Block(reader, BlockLayout.Normal);
                                        break;
                                    case PixelDataFormat.SpecialFormatDXT5:
                                        decompressedBlock = DecodeDXT5Block(reader, BlockLayout.Normal);
                                        break;
                                    case PixelDataFormat.SpecialFormatRGTC1:
                                        decompressedBlock = DecodeRGTC1Block(reader, BlockLayout.Normal, Signedness.Unsigned);
                                        break;
                                    case PixelDataFormat.SpecialFormatRGTC1_Signed:
                                        decompressedBlock = DecodeRGTC1Block(reader, BlockLayout.Normal, Signedness.Signed);
                                        break;
                                    case PixelDataFormat.SpecialFormatRGTC2:
                                        decompressedBlock = DecodeRGTC2Block(reader, BlockLayout.Normal, Signedness.Unsigned);
                                        break;
                                    case PixelDataFormat.SpecialFormatRGTC2_Signed:
                                        decompressedBlock = DecodeRGTC2Block(reader, BlockLayout.Normal, Signedness.Signed);
                                        break;
                                    case PixelDataFormat.SpecialFormatBPTC:
                                        decompressedBlock = new byte[64];
                                        Array.Clear(decompressedBlock, 0, decompressedBlock.Length);
                                        reader.ReadBytes(16);
                                        break;
                                    default:
                                        throw new Exception("不支持的DXT/RGTC格式");
                                }

                                int rx = (x / 4) * 4;
                                int ry = (y / 4) * 4;

                                for (int py = 0; py < 4; py++)
                                {
                                    for (int px = 0; px < 4; px++)
                                    {
                                        int ix = (rx + px);
                                        int iy = (ry + py);

                                        if (ix >= width || iy >= height) continue;

                                        for (int c = 0; c < 4; c++)
                                            outPixels[(((iy * width) + ix) * 4) + c] = decompressedBlock[(((py * 4) + px) * 4) + c];
                                    }
                                }
                            }
                        }
                    }
                }

                return outPixels;
            }

            private static byte[] DecodeColorBlock(ushort color0, ushort color1, byte[] bits, bool has1bitAlpha)
            {
                byte[] outData = new byte[64];
                byte[,] colors = new byte[4, 4];

                UnpackRgb565(color0, out colors[0, 2], out colors[0, 1], out colors[0, 0]);
                UnpackRgb565(color1, out colors[1, 2], out colors[1, 1], out colors[1, 0]);
                colors[0, 3] = 255;
                colors[1, 3] = 255;

                if (color0 <= color1)
                {
                    colors[2, 0] = (byte)((colors[0, 0] + colors[1, 0]) / 2);
                    colors[2, 1] = (byte)((colors[0, 1] + colors[1, 1]) / 2);
                    colors[2, 2] = (byte)((colors[0, 2] + colors[1, 2]) / 2);
                    colors[2, 3] = 255;

                    colors[3, 0] = 0;
                    colors[3, 1] = 0;
                    colors[3, 2] = 0;
                    colors[3, 3] = (byte)((has1bitAlpha && color0 <= color1) ? 0 : 0xFF);
                }
                else
                {
                    colors[2, 0] = (byte)((2 * colors[0, 0] + colors[1, 0]) / 3);
                    colors[2, 1] = (byte)((2 * colors[0, 1] + colors[1, 1]) / 3);
                    colors[2, 2] = (byte)((2 * colors[0, 2] + colors[1, 2]) / 3);
                    colors[2, 3] = 255;

                    colors[3, 0] = (byte)((colors[0, 0] + 2 * colors[1, 0]) / 3);
                    colors[3, 1] = (byte)((colors[0, 1] + 2 * colors[1, 1]) / 3);
                    colors[3, 2] = (byte)((colors[0, 2] + 2 * colors[1, 2]) / 3);
                    colors[3, 3] = 255;
                }

                for (int by = 0; by < 4; by++)
                {
                    for (int bx = 0; bx < 4; bx++)
                    {
                        byte code = bits[(by * 4) + bx];
                        for (int c = 0; c < 4; c++)
                            outData[(((by * 4) + bx) * 4) + c] = colors[code, c];
                    }
                }

                return outData;
            }

            private static byte[] DecodeDXT1Block(BinaryReader reader, BlockLayout blockLayout, bool has1bitAlpha)
            {
                byte color0_hi, color0_lo, color1_hi, color1_lo, bits_3, bits_2, bits_1, bits_0;

                if (blockLayout == BlockLayout.Normal)
                {
                    color0_hi = reader.ReadByte();
                    color0_lo = reader.ReadByte();
                    color1_hi = reader.ReadByte();
                    color1_lo = reader.ReadByte();
                    bits_3 = reader.ReadByte();
                    bits_2 = reader.ReadByte();
                    bits_1 = reader.ReadByte();
                    bits_0 = reader.ReadByte();
                }
                else
                {
                    bits_3 = reader.ReadByte();
                    bits_2 = reader.ReadByte();
                    bits_1 = reader.ReadByte();
                    bits_0 = reader.ReadByte();
                    color0_hi = reader.ReadByte();
                    color0_lo = reader.ReadByte();
                    color1_hi = reader.ReadByte();
                    color1_lo = reader.ReadByte();
                }

                byte[] bits = ExtractBits((((uint)bits_0 << 24) | ((uint)bits_1 << 16) | ((uint)bits_2 << 8) | (uint)bits_3), 2);
                ushort color0 = (ushort)(((ushort)color0_lo << 8) | (ushort)color0_hi);
                ushort color1 = (ushort)(((ushort)color1_lo << 8) | (ushort)color1_hi);

                return DecodeColorBlock(color0, color1, bits, has1bitAlpha);
            }

            private static byte[] DecodeDXT3Block(BinaryReader reader, BlockLayout blockLayout)
            {
                ulong alpha;
                byte[] colorBlock;

                if (blockLayout == BlockLayout.Normal)
                {
                    alpha = reader.ReadUInt64();
                    colorBlock = DecodeDXT1Block(reader, blockLayout, false);
                }
                else
                {
                    colorBlock = DecodeDXT1Block(reader, blockLayout, false);
                    alpha = reader.ReadUInt64();
                }

                for (int i = 0; i < colorBlock.Length; i += 4)
                {
                    colorBlock[i + 3] = (byte)(((alpha & 0xF) << 4) | (alpha & 0xF));
                    alpha >>= 4;
                }

                return colorBlock;
            }

            private static byte[] DecodeDXT5Block(BinaryReader reader, BlockLayout blockLayout)
            {
                byte alpha0, alpha1;
                byte[] alphaBits = new byte[16];
                byte[] colorBlock;

                if (blockLayout == BlockLayout.Normal)
                {
                    alpha0 = reader.ReadByte();
                    alpha1 = reader.ReadByte();
                    byte bits_5 = reader.ReadByte();
                    byte bits_4 = reader.ReadByte();
                    byte bits_3 = reader.ReadByte();
                    byte bits_2 = reader.ReadByte();
                    byte bits_1 = reader.ReadByte();
                    byte bits_0 = reader.ReadByte();
                    alphaBits = ExtractBits((((ulong)bits_0 << 40) | ((ulong)bits_1 << 32) | ((ulong)bits_2 << 24) | ((ulong)bits_3 << 16) | ((ulong)bits_4 << 8) | (ulong)bits_5), 3);
                    colorBlock = DecodeDXT1Block(reader, blockLayout, false);
                }
                else
                {
                    colorBlock = DecodeDXT1Block(reader, blockLayout, false);
                    alpha0 = reader.ReadByte();
                    alpha1 = reader.ReadByte();
                    byte bits_5 = reader.ReadByte();
                    byte bits_4 = reader.ReadByte();
                    byte bits_3 = reader.ReadByte();
                    byte bits_2 = reader.ReadByte();
                    byte bits_1 = reader.ReadByte();
                    byte bits_0 = reader.ReadByte();
                    alphaBits = ExtractBits((((ulong)bits_0 << 40) | ((ulong)bits_1 << 32) | ((ulong)bits_2 << 24) | ((ulong)bits_3 << 16) | ((ulong)bits_4 << 8) | (ulong)bits_5), 3);
                }

                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        byte code = alphaBits[(y * 4) + x];
                        int destOffset = (((y * 4) + x) * 4) + 3;

                        if (alpha0 > alpha1)
                        {
                            switch (code)
                            {
                                case 0x00: colorBlock[destOffset] = alpha0; break;
                                case 0x01: colorBlock[destOffset] = alpha1; break;
                                case 0x02: colorBlock[destOffset] = (byte)((6 * alpha0 + 1 * alpha1) / 7); break;
                                case 0x03: colorBlock[destOffset] = (byte)((5 * alpha0 + 2 * alpha1) / 7); break;
                                case 0x04: colorBlock[destOffset] = (byte)((4 * alpha0 + 3 * alpha1) / 7); break;
                                case 0x05: colorBlock[destOffset] = (byte)((3 * alpha0 + 4 * alpha1) / 7); break;
                                case 0x06: colorBlock[destOffset] = (byte)((2 * alpha0 + 5 * alpha1) / 7); break;
                                case 0x07: colorBlock[destOffset] = (byte)((1 * alpha0 + 6 * alpha1) / 7); break;
                            }
                        }
                        else
                        {
                            switch (code)
                            {
                                case 0x00: colorBlock[destOffset] = alpha0; break;
                                case 0x01: colorBlock[destOffset] = alpha1; break;
                                case 0x02: colorBlock[destOffset] = (byte)((4 * alpha0 + 1 * alpha1) / 5); break;
                                case 0x03: colorBlock[destOffset] = (byte)((3 * alpha0 + 2 * alpha1) / 5); break;
                                case 0x04: colorBlock[destOffset] = (byte)((2 * alpha0 + 3 * alpha1) / 5); break;
                                case 0x05: colorBlock[destOffset] = (byte)((1 * alpha0 + 4 * alpha1) / 5); break;
                                case 0x06: colorBlock[destOffset] = 0x00; break;
                                case 0x07: colorBlock[destOffset] = 0xFF; break;
                            }
                        }
                    }
                }

                return colorBlock;
            }

            private static byte[] DecodeRGTC1Block(BinaryReader reader, BlockLayout blockLayout, Signedness signedness)
            {
                byte data0 = reader.ReadByte();
                byte data1 = reader.ReadByte();
                byte bits_5 = reader.ReadByte();
                byte bits_4 = reader.ReadByte();
                byte bits_3 = reader.ReadByte();
                byte bits_2 = reader.ReadByte();
                byte bits_1 = reader.ReadByte();
                byte bits_0 = reader.ReadByte();
                byte[] bits = ExtractBits((((ulong)bits_0 << 40) | ((ulong)bits_1 << 32) | ((ulong)bits_2 << 24) | ((ulong)bits_3 << 16) | ((ulong)bits_4 << 8) | (ulong)bits_5), 3);

                byte[] outData = new byte[64];
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int destOffset = (((y * 4) + x) * 4);
                        outData[destOffset + 0] = 0x00;
                        outData[destOffset + 1] = 0x00;
                        outData[destOffset + 2] = DecodeRGTCValue(data0, data1, bits[(y * 4) + x], signedness);
                        outData[destOffset + 3] = 0xFF;
                    }
                }
                return outData;
            }

            private static byte[] DecodeRGTC2Block(BinaryReader reader, BlockLayout blockLayout, Signedness signedness)
            {
                byte data0Red = reader.ReadByte();
                byte data1Red = reader.ReadByte();
                byte bits_5Red = reader.ReadByte();
                byte bits_4Red = reader.ReadByte();
                byte bits_3Red = reader.ReadByte();
                byte bits_2Red = reader.ReadByte();
                byte bits_1Red = reader.ReadByte();
                byte bits_0Red = reader.ReadByte();
                byte[] bitsRed = ExtractBits((((ulong)bits_0Red << 40) | ((ulong)bits_1Red << 32) | ((ulong)bits_2Red << 24) | ((ulong)bits_3Red << 16) | ((ulong)bits_4Red << 8) | (ulong)bits_5Red), 3);

                byte data0Green = reader.ReadByte();
                byte data1Green = reader.ReadByte();
                byte bits_5Green = reader.ReadByte();
                byte bits_4Green = reader.ReadByte();
                byte bits_3Green = reader.ReadByte();
                byte bits_2Green = reader.ReadByte();
                byte bits_1Green = reader.ReadByte();
                byte bits_0Green = reader.ReadByte();
                byte[] bitsGreen = ExtractBits((((ulong)bits_0Green << 40) | ((ulong)bits_1Green << 32) | ((ulong)bits_2Green << 24) | ((ulong)bits_3Green << 16) | ((ulong)bits_4Green << 8) | (ulong)bits_5Green), 3);

                byte[] outData = new byte[64];
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int destOffset = (((y * 4) + x) * 4);
                        outData[destOffset + 0] = 0x00;
                        outData[destOffset + 1] = DecodeRGTCValue(data0Green, data1Green, bitsGreen[(y * 4) + x], signedness);
                        outData[destOffset + 2] = DecodeRGTCValue(data0Red, data1Red, bitsRed[(y * 4) + x], signedness);
                        outData[destOffset + 3] = 0xFF;
                    }
                }
                return outData;
            }

            private static byte DecodeRGTCValue(byte data0, byte data1, byte code, Signedness signedness)
            {
                byte d0, d1;
                if (signedness == Signedness.Unsigned)
                {
                    d0 = data0;
                    d1 = data1;
                }
                else
                {
                    d0 = (byte)(((sbyte)data0) + 128);
                    d1 = (byte)(((sbyte)data1) + 128);
                }

                if (d0 > d1)
                {
                    switch (code)
                    {
                        case 0x00: return d0;
                        case 0x01: return d1;
                        case 0x02: return (byte)((6 * d0 + 1 * d1) / 7);
                        case 0x03: return (byte)((5 * d0 + 2 * d1) / 7);
                        case 0x04: return (byte)((4 * d0 + 3 * d1) / 7);
                        case 0x05: return (byte)((3 * d0 + 4 * d1) / 7);
                        case 0x06: return (byte)((2 * d0 + 5 * d1) / 7);
                        case 0x07: return (byte)((1 * d0 + 6 * d1) / 7);
                    }
                }
                else
                {
                    switch (code)
                    {
                        case 0x00: return d0;
                        case 0x01: return d1;
                        case 0x02: return (byte)((4 * d0 + 1 * d1) / 5);
                        case 0x03: return (byte)((3 * d0 + 2 * d1) / 5);
                        case 0x04: return (byte)((2 * d0 + 3 * d1) / 5);
                        case 0x05: return (byte)((1 * d0 + 4 * d1) / 5);
                        case 0x06: return 0x00;
                        case 0x07: return 0xFF;
                    }
                }
                throw new Exception("RGTC值解码异常");
            }

            public static byte[] ExtractBits(ulong bits, int numBits)
            {
                byte[] bitsExt = new byte[16];
                for (int i = 0; i < bitsExt.Length; i++)
                    bitsExt[i] = (byte)((bits >> (i * numBits)) & (byte)((1 << numBits) - 1));
                return bitsExt;
            }

            private static void UnpackRgb565(ushort rgb565, out byte r, out byte g, out byte b)
            {
                r = (byte)((rgb565 & 0xF800) >> 11);
                r = (byte)((r << 3) | (r >> 2));
                g = (byte)((rgb565 & 0x07E0) >> 5);
                g = (byte)((g << 2) | (g >> 4));
                b = (byte)(rgb565 & 0x1F);
                b = (byte)((b << 3) | (b >> 2));
            }
        }
    }
}