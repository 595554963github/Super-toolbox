using System.Drawing.Imaging;

namespace super_toolbox
{
    public class Tpl2png_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private const uint TPL_MAGIC = 0x0020AF30;

        private enum TextureFormat
        {
            I4 = 0,
            I8 = 1,
            IA4 = 2,
            IA8 = 3,
            RGB565 = 4,
            RGB5A3 = 5,
            RGBA8 = 6,
            CI4 = 8,
            CI8 = 9,
            CI14X2 = 10,
            CMPR = 14
        }

        private class TPLHeader
        {
            public uint Magic { get; set; }
            public uint NumImages { get; set; }
            public uint TableOffset { get; set; }
        }

        private class ImageHeader
        {
            public ushort Height { get; set; }
            public ushort Width { get; set; }
            public TextureFormat Format { get; set; }
            public uint DataOffset { get; set; }
            public uint WrapS { get; set; }
            public uint WrapT { get; set; }
            public uint MinFilter { get; set; }
            public uint MagFilter { get; set; }
            public float LODBias { get; set; }
            public byte EdgeLOD { get; set; }
            public byte MidLOD { get; set; }
            public byte MaxLOD { get; set; }
            public byte Unpacked { get; set; }
            public uint PaletteOffset { get; set; }
            public ushort PaletteEntries { get; set; }
            public ushort PaletteFormat { get; set; }
        }

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

            var tplFiles = Directory.EnumerateFiles(directoryPath, "*.tpl", SearchOption.AllDirectories).ToList();

            TotalFilesToConvert = tplFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var filePath in tplFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");

                    try
                    {
                        int converted = await ConvertTplToPngAsync(filePath, cancellationToken);
                        if (converted > 0)
                        {
                            successCount++;
                            convertedFiles.Add(filePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(filePath)}->{converted}个png");
                            OnFileConverted(filePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{Path.GetFileName(filePath)}转换失败");
                            OnConversionFailed($"{Path.GetFileName(filePath)}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{Path.GetFileName(filePath)} 处理错误:{ex.Message}");
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

        private async Task<int> ConvertTplToPngAsync(string inputPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(inputPath, cancellationToken);

                if (fileData.Length < 12)
                {
                    ConversionError?.Invoke(this, "文件太小,不是有效的TPL文件");
                    return 0;
                }

                TPLHeader header = ParseHeader(fileData);

                if (header.Magic != TPL_MAGIC)
                {
                    ConversionError?.Invoke(this, $"无效的TPL文件,魔数:0x{header.Magic:X}");
                    return 0;
                }

                ConversionProgress?.Invoke(this, $"TPL信息:{header.NumImages}个纹理");

                List<Bitmap> images = new List<Bitmap>();
                uint tableOffset = header.TableOffset;
                string fileDirectory = Path.GetDirectoryName(inputPath) ?? string.Empty;

                for (int i = 0; i < header.NumImages; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint entryOffset = tableOffset + (uint)(i * 8);
                    if (entryOffset + 8 > fileData.Length)
                        break;

                    uint imageOffset = BitConverter.ToUInt32(fileData, (int)entryOffset);
                    imageOffset = SwapEndianness(imageOffset);

                    uint paletteOffset = BitConverter.ToUInt32(fileData, (int)entryOffset + 4);
                    paletteOffset = SwapEndianness(paletteOffset);

                    ImageHeader imageHeader = ParseImageHeader(fileData, imageOffset);

                    List<Color>? palette = null;
                    if (imageHeader.Format == TextureFormat.CI4 || imageHeader.Format == TextureFormat.CI8)
                    {
                        if (paletteOffset != 0 && imageHeader.PaletteEntries > 0)
                        {
                            palette = ParsePalette(fileData, imageHeader.PaletteOffset, imageHeader.PaletteEntries, imageHeader.PaletteFormat);
                        }
                    }

                    Bitmap? bitmap = DecodeTexture(fileData, imageHeader, palette);
                    if (bitmap != null)
                    {
                        images.Add(bitmap);
                    }
                }

                for (int i = 0; i < images.Count; i++)
                {
                    string outputPath;
                    if (images.Count == 1)
                    {
                        string baseName = Path.GetFileNameWithoutExtension(inputPath);
                        outputPath = Path.Combine(fileDirectory, $"{baseName}.png");
                    }
                    else
                    {
                        outputPath = Path.Combine(fileDirectory, $"{Path.GetFileNameWithoutExtension(inputPath)}_{i:D4}.png");
                    }

                    images[i].Save(outputPath, ImageFormat.Png);
                    images[i].Dispose();

                    ConversionProgress?.Invoke(this, $"已保存:{Path.GetFileName(outputPath)}");
                }

                return images.Count;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return 0;
            }
        }

        private TPLHeader ParseHeader(byte[] data)
        {
            uint magic = SwapEndianness(BitConverter.ToUInt32(data, 0));
            uint numImages = SwapEndianness(BitConverter.ToUInt32(data, 4));
            uint tableOffset = SwapEndianness(BitConverter.ToUInt32(data, 8));

            return new TPLHeader
            {
                Magic = magic,
                NumImages = numImages,
                TableOffset = tableOffset
            };
        }

        private ImageHeader ParseImageHeader(byte[] data, uint offset)
        {
            ushort height = SwapEndianness(BitConverter.ToUInt16(data, (int)offset));
            ushort width = SwapEndianness(BitConverter.ToUInt16(data, (int)offset + 2));
            uint format = SwapEndianness(BitConverter.ToUInt32(data, (int)offset + 4));
            uint dataOffset = SwapEndianness(BitConverter.ToUInt32(data, (int)offset + 8));
            uint wrapS = SwapEndianness(BitConverter.ToUInt32(data, (int)offset + 12));
            uint wrapT = SwapEndianness(BitConverter.ToUInt32(data, (int)offset + 16));
            uint minFilter = SwapEndianness(BitConverter.ToUInt32(data, (int)offset + 20));
            uint magFilter = SwapEndianness(BitConverter.ToUInt32(data, (int)offset + 24));
            float lodBias = BitConverter.ToSingle(data, (int)offset + 28);
            byte edgeLod = data[offset + 32];
            byte midLod = data[offset + 33];
            byte maxLod = data[offset + 34];
            byte unpacked = data[offset + 35];

            uint paletteOffset = 0;
            ushort paletteEntries = 0;
            ushort paletteFormat = 0;

            if ((TextureFormat)format == TextureFormat.CI4 ||
                (TextureFormat)format == TextureFormat.CI8 ||
                (TextureFormat)format == TextureFormat.CI14X2)
            {
                if (offset + 44 <= data.Length)
                {
                    paletteOffset = SwapEndianness(BitConverter.ToUInt32(data, (int)offset + 36));
                    paletteEntries = SwapEndianness(BitConverter.ToUInt16(data, (int)offset + 40));
                    paletteFormat = SwapEndianness(BitConverter.ToUInt16(data, (int)offset + 42));
                }
            }

            return new ImageHeader
            {
                Height = height,
                Width = width,
                Format = (TextureFormat)format,
                DataOffset = dataOffset,
                WrapS = wrapS,
                WrapT = wrapT,
                MinFilter = minFilter,
                MagFilter = magFilter,
                LODBias = lodBias,
                EdgeLOD = edgeLod,
                MidLOD = midLod,
                MaxLOD = maxLod,
                Unpacked = unpacked,
                PaletteOffset = paletteOffset,
                PaletteEntries = paletteEntries,
                PaletteFormat = paletteFormat
            };
        }

        private List<Color> ParsePalette(byte[] data, uint offset, ushort entries, ushort format)
        {
            List<Color> palette = new List<Color>();

            for (int i = 0; i < entries; i++)
            {
                if (offset + (i * 2) + 2 > data.Length)
                    break;

                ushort entry = SwapEndianness(BitConverter.ToUInt16(data, (int)offset + i * 2));

                if (format == 0)
                {
                    byte r = (byte)((entry >> 8) & 0xFF);
                    byte g = (byte)((entry >> 8) & 0xFF);
                    byte b = (byte)((entry >> 8) & 0xFF);
                    byte a = (byte)(entry & 0xFF);
                    palette.Add(Color.FromArgb(a, r, g, b));
                }
                else if (format == 1)
                {
                    int r5 = (entry >> 11) & 0x1F;
                    int g6 = (entry >> 5) & 0x3F;
                    int b5 = entry & 0x1F;
                    byte r = (byte)((r5 << 3) | (r5 >> 2));
                    byte g = (byte)((g6 << 2) | (g6 >> 4));
                    byte b = (byte)((b5 << 3) | (b5 >> 2));
                    palette.Add(Color.FromArgb(255, r, g, b));
                }
                else if (format == 2)
                {
                    if ((entry & 0x8000) != 0)
                    {
                        int r5 = (entry >> 10) & 0x1F;
                        int g5 = (entry >> 5) & 0x1F;
                        int b5 = entry & 0x1F;
                        byte r = (byte)((r5 << 3) | (r5 >> 2));
                        byte g = (byte)((g5 << 3) | (g5 >> 2));
                        byte b = (byte)((b5 << 3) | (b5 >> 2));
                        palette.Add(Color.FromArgb(255, r, g, b));
                    }
                    else
                    {
                        int a3 = (entry >> 12) & 0x7;
                        int r4 = (entry >> 8) & 0xF;
                        int g4 = (entry >> 4) & 0xF;
                        int b4 = entry & 0xF;
                        byte a = (byte)((a3 << 5) | (a3 << 2) | (a3 >> 1));
                        byte r = (byte)(r4 * 17);
                        byte g = (byte)(g4 * 17);
                        byte b = (byte)(b4 * 17);
                        palette.Add(Color.FromArgb(a, r, g, b));
                    }
                }
            }

            return palette;
        }

        private Bitmap? DecodeTexture(byte[] data, ImageHeader header, List<Color>? palette)
        {
            try
            {
                int width = header.Width;
                int height = header.Height;
                uint dataOffset = header.DataOffset;

                switch (header.Format)
                {
                    case TextureFormat.I4:
                        return DecodeI4(data, dataOffset, width, height);
                    case TextureFormat.I8:
                        return DecodeI8(data, dataOffset, width, height);
                    case TextureFormat.IA4:
                        return DecodeIA4(data, dataOffset, width, height);
                    case TextureFormat.IA8:
                        return DecodeIA8(data, dataOffset, width, height);
                    case TextureFormat.RGB565:
                        return DecodeRGB565(data, dataOffset, width, height);
                    case TextureFormat.RGB5A3:
                        return DecodeRGB5A3(data, dataOffset, width, height);
                    case TextureFormat.RGBA8:
                        return DecodeRGBA8(data, dataOffset, width, height);
                    case TextureFormat.CI4:
                        return DecodeCI4(data, dataOffset, width, height, palette);
                    case TextureFormat.CI8:
                        return DecodeCI8(data, dataOffset, width, height, palette);
                    case TextureFormat.CMPR:
                        return DecodeCMPR(data, dataOffset, width, height);
                    default:
                        ConversionError?.Invoke(this, $"不支持的格式:{header.Format}");
                        return null;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码纹理失败:{ex.Message}");
                return null;
            }
        }

        private Bitmap DecodeI4(byte[] data, uint offset, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 8)
            {
                for (int xTile = 0; xTile < width; xTile += 8)
                {
                    for (int y = yTile; y < yTile + 8 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 8 && x < width; x += 2)
                        {
                            if (i >= data.Length) break;
                            byte b = data[i];

                            int val = (b >> 4) & 0xF;
                            int intensity = val * 17;
                            if (y < height && x < width)
                            {
                                bitmap.SetPixel(x, y, Color.FromArgb(255, intensity, intensity, intensity));
                            }

                            val = b & 0xF;
                            intensity = val * 17;
                            if (y < height && x + 1 < width)
                            {
                                bitmap.SetPixel(x + 1, y, Color.FromArgb(255, intensity, intensity, intensity));
                            }

                            i++;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeI8(byte[] data, uint offset, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 4)
            {
                for (int xTile = 0; xTile < width; xTile += 8)
                {
                    for (int y = yTile; y < yTile + 4 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 8 && x < width; x++)
                        {
                            if (i >= data.Length) break;
                            int val = data[i];
                            bitmap.SetPixel(x, y, Color.FromArgb(255, val, val, val));
                            i++;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeIA4(byte[] data, uint offset, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 4)
            {
                for (int xTile = 0; xTile < width; xTile += 8)
                {
                    for (int y = yTile; y < yTile + 4 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 8 && x < width; x++)
                        {
                            if (i >= data.Length) break;
                            byte b = data[i];
                            int alpha = ((b >> 4) & 0xF) * 17;
                            int intensity = (b & 0xF) * 17;
                            bitmap.SetPixel(x, y, Color.FromArgb(alpha, intensity, intensity, intensity));
                            i++;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeIA8(byte[] data, uint offset, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 4)
            {
                for (int xTile = 0; xTile < width; xTile += 4)
                {
                    for (int y = yTile; y < yTile + 4 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 4 && x < width; x++)
                        {
                            if (i + 1 >= data.Length) break;
                            int intensity = data[i];
                            int alpha = data[i + 1];
                            bitmap.SetPixel(x, y, Color.FromArgb(alpha, intensity, intensity, intensity));
                            i += 2;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeRGB565(byte[] data, uint offset, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 4)
            {
                for (int xTile = 0; xTile < width; xTile += 4)
                {
                    for (int y = yTile; y < yTile + 4 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 4 && x < width; x++)
                        {
                            if (i + 1 >= data.Length) break;
                            int val = (data[i] << 8) | data[i + 1];

                            int r5 = (val >> 11) & 0x1F;
                            int g6 = (val >> 5) & 0x3F;
                            int b5 = val & 0x1F;

                            int r = (r5 << 3) | (r5 >> 2);
                            int g = (g6 << 2) | (g6 >> 4);
                            int b = (b5 << 3) | (b5 >> 2);

                            bitmap.SetPixel(x, y, Color.FromArgb(255, r, g, b));
                            i += 2;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeRGB5A3(byte[] data, uint offset, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 4)
            {
                for (int xTile = 0; xTile < width; xTile += 4)
                {
                    for (int y = yTile; y < yTile + 4 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 4 && x < width; x++)
                        {
                            if (i + 1 >= data.Length) break;
                            int val = (data[i] << 8) | data[i + 1];

                            int r, g, b, a;

                            if ((val & 0x8000) != 0)
                            {
                                int r5 = (val >> 10) & 0x1F;
                                int g5 = (val >> 5) & 0x1F;
                                int b5 = val & 0x1F;
                                r = (r5 << 3) | (r5 >> 2);
                                g = (g5 << 3) | (g5 >> 2);
                                b = (b5 << 3) | (b5 >> 2);
                                a = 255;
                            }
                            else
                            {
                                int a3 = (val >> 12) & 0x7;
                                int r4 = (val >> 8) & 0xF;
                                int g4 = (val >> 4) & 0xF;
                                int b4 = val & 0xF;
                                a = (a3 << 5) | (a3 << 2) | (a3 >> 1);
                                r = r4 * 17;
                                g = g4 * 17;
                                b = b4 * 17;
                            }

                            bitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                            i += 2;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeRGBA8(byte[] data, uint offset, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 4)
            {
                for (int xTile = 0; xTile < width; xTile += 4)
                {
                    List<byte> ar = new List<byte>();
                    List<byte> gb = new List<byte>();

                    for (int j = 0; j < 16; j++)
                    {
                        if (i + 1 >= data.Length) break;
                        ar.Add(data[i]);
                        ar.Add(data[i + 1]);
                        i += 2;
                    }

                    for (int j = 0; j < 16; j++)
                    {
                        if (i + 1 >= data.Length) break;
                        gb.Add(data[i]);
                        gb.Add(data[i + 1]);
                        i += 2;
                    }

                    int idx = 0;
                    for (int y = yTile; y < yTile + 4 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 4 && x < width; x++)
                        {
                            if (idx * 2 + 1 < ar.Count && idx * 2 + 1 < gb.Count)
                            {
                                int a = ar[idx * 2];
                                int r = ar[idx * 2 + 1];
                                int g = gb[idx * 2];
                                int b = gb[idx * 2 + 1];
                                bitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                            }
                            idx++;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeCI4(byte[] data, uint offset, int width, int height, List<Color>? palette)
        {
            if (palette == null || palette.Count == 0)
                throw new InvalidOperationException("CI4格式需要调色板");

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 8)
            {
                for (int xTile = 0; xTile < width; xTile += 8)
                {
                    for (int y = yTile; y < yTile + 8 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 8 && x < width; x += 2)
                        {
                            if (i >= data.Length) break;
                            byte b = data[i];

                            int idx = (b >> 4) & 0xF;
                            if (idx < palette.Count)
                            {
                                if (y < height && x < width)
                                    bitmap.SetPixel(x, y, palette[idx]);
                            }

                            idx = b & 0xF;
                            if (idx < palette.Count)
                            {
                                if (y < height && x + 1 < width)
                                    bitmap.SetPixel(x + 1, y, palette[idx]);
                            }

                            i++;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeCI8(byte[] data, uint offset, int width, int height, List<Color>? palette)
        {
            if (palette == null || palette.Count == 0)
                throw new InvalidOperationException("CI8格式需要调色板");

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 4)
            {
                for (int xTile = 0; xTile < width; xTile += 8)
                {
                    for (int y = yTile; y < yTile + 4 && y < height; y++)
                    {
                        for (int x = xTile; x < xTile + 8 && x < width; x++)
                        {
                            if (i >= data.Length) break;
                            int idx = data[i];
                            if (idx < palette.Count)
                                bitmap.SetPixel(x, y, palette[idx]);
                            i++;
                        }
                    }
                }
            }
            return bitmap;
        }

        private Bitmap DecodeCMPR(byte[] data, uint offset, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            int i = (int)offset;

            for (int yTile = 0; yTile < height; yTile += 8)
            {
                for (int xTile = 0; xTile < width; xTile += 8)
                {
                    for (int subY = 0; subY < 8; subY += 4)
                    {
                        for (int subX = 0; subX < 8; subX += 4)
                        {
                            if (i + 4 >= data.Length) break;

                            int val1 = (data[i] << 8) | data[i + 1];
                            int val2 = (data[i + 2] << 8) | data[i + 3];

                            int r1 = ((val1 >> 11) & 0x1F) << 3;
                            int g1 = ((val1 >> 5) & 0x3F) << 2;
                            int b1 = (val1 & 0x1F) << 3;

                            int r2 = ((val2 >> 11) & 0x1F) << 3;
                            int g2 = ((val2 >> 5) & 0x3F) << 2;
                            int b2 = (val2 & 0x1F) << 3;

                            Color[] colors;

                            if (val1 > val2)
                            {
                                colors = new Color[]
                                {
                                    Color.FromArgb(255, r1, g1, b1),
                                    Color.FromArgb(255, r2, g2, b2),
                                    Color.FromArgb(255, (2*r1 + r2)/3, (2*g1 + g2)/3, (2*b1 + b2)/3),
                                    Color.FromArgb(255, (2*r2 + r1)/3, (2*g2 + g1)/3, (2*b2 + b1)/3)
                                };
                            }
                            else
                            {
                                colors = new Color[]
                                {
                                    Color.FromArgb(255, r1, g1, b1),
                                    Color.FromArgb(255, r2, g2, b2),
                                    Color.FromArgb(255, (r1 + r2)/2, (g1 + g2)/2, (b1 + b2)/2),
                                    Color.FromArgb(0, 0, 0, 0)
                                };
                            }

                            i += 4;

                            for (int yb = 0; yb < 4; yb++)
                            {
                                if (i >= data.Length) break;
                                int indices = data[i];
                                i++;

                                for (int xb = 0; xb < 4; xb++)
                                {
                                    int idx = (indices >> (6 - xb * 2)) & 3;
                                    int y = yTile + subY + yb;
                                    int x = xTile + subX + (3 - xb);
                                    if (y < height && x < width)
                                    {
                                        bitmap.SetPixel(x, y, colors[idx]);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return bitmap;
        }

        private uint SwapEndianness(uint value)
        {
            return ((value & 0xFF000000) >> 24) |
                   ((value & 0x00FF0000) >> 8) |
                   ((value & 0x0000FF00) << 8) |
                   ((value & 0x000000FF) << 24);
        }

        private ushort SwapEndianness(ushort value)
        {
            return (ushort)((value >> 8) | (value << 8));
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}