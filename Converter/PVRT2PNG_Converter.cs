using System.Drawing.Imaging;

namespace super_toolbox
{
    public class PVRT2PNG_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

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
            var pvrFiles = Directory.EnumerateFiles(directoryPath, "*.pvr", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = pvrFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var filePath in pvrFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    try
                    {
                        bool conversionSuccess = await ConvertPvrToPngAsync(filePath, pngFilePath, cancellationToken);
                        if (conversionSuccess && File.Exists(pngFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(pngFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngFilePath)}");
                            OnFileConverted(pngFilePath);
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
                    ConversionProgress?.Invoke(this, $"转换完成，成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成，但未成功转换任何文件");
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

        private async Task<bool> ConvertPvrToPngAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(inputPath, cancellationToken);

                using (MemoryStream ms = new MemoryStream(fileData))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    int gbixOffset = FindGBIXOffset(fileData);

                    if (gbixOffset != -1)
                    {
                        ms.Position = gbixOffset + 4;
                        uint gbixSize = reader.ReadUInt32();

                        if (gbixSize == 0x08)
                        {
                            uint gbixVal1 = reader.ReadUInt32();
                            uint gbixVal2 = reader.ReadUInt32();
                        }
                        else if (gbixSize == 0x04)
                        {
                            uint gbixVal1 = reader.ReadUInt32();
                        }
                    }

                    int pvrtOffset = FindPVRTOffset(fileData);
                    if (pvrtOffset == -1)
                    {
                        ConversionError?.Invoke(this, "无效的PVR文件:未找到PVRT标识");
                        return false;
                    }

                    ms.Position = pvrtOffset + 8;
                    byte pixelFormat = reader.ReadByte();
                    byte textureFormat = reader.ReadByte();
                    ms.Position += 2;
                    ushort width = reader.ReadUInt16();
                    ushort height = reader.ReadUInt16();

                    long dataOffset = ms.Position;

                    Bitmap? bitmap = DecodePVR(fileData, width, height, pixelFormat, textureFormat, dataOffset);

                    if (bitmap == null)
                    {
                        ConversionError?.Invoke(this, "解码PVR数据失败");
                        return false;
                    }

                    string? directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    bitmap.Save(outputPath, ImageFormat.Png);
                    bitmap.Dispose();
                    return true;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                }
                return false;
            }
        }

        private int FindGBIXOffset(byte[] data)
        {
            byte[] gbixPattern = new byte[] { (byte)'G', (byte)'B', (byte)'I', (byte)'X' };
            return FindPattern(data, gbixPattern);
        }

        private int FindPVRTOffset(byte[] data)
        {
            byte[] pvrtPattern = new byte[] { (byte)'P', (byte)'V', (byte)'R', (byte)'T' };
            return FindPattern(data, pvrtPattern);
        }

        private int FindPattern(byte[] data, byte[] pattern)
        {
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        private Bitmap? DecodePVR(byte[] fileData, int width, int height, byte pixelFormat, byte textureFormat, long dataOffset)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(fileData))
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    ms.Position = dataOffset;

                    Dictionary<byte, string> pxModes = new Dictionary<byte, string>
                    {
                        {0, "ARGB1555"}, {1, "RGB565"}, {2, "ARGB4444"},
                        {3, "YUV422"}, {4, "BUMP"}, {5, "RGB555"},
                        {6, "YUV420"}, {7, "ARGB8888"}, {8, "PAL-4"},
                        {9, "PAL-8"}, {10, "AUTO"}
                    };

                    Dictionary<byte, string> texModes = new Dictionary<byte, string>
                    {
                        {1, "Twiddled"}, {2, "Twiddled + Mips"}, {3, "Twiddled VQ"},
                        {4, "Twiddled VQ + Mips"}, {5, "Twiddled Pal4"}, {6, "Twiddled Pal4 + Mips"},
                        {7, "Twiddled Pal8"}, {8, "Twiddled Pal8 + Mips"}, {9, "Rectangle"},
                        {10, "Rectangle + Mips"}, {11, "Stride"}, {12, "Stride + Mips"},
                        {13, "Twiddled Rectangle"}, {14, "BMP"}, {15, "BMP + Mips"},
                        {16, "Twiddled SmallVQ"}, {17, "Twiddled SmallVQ + Mips"}, {18, "Twiddled Alias + Mips"}
                    };

                    if (textureFormat >= 5 && textureFormat <= 8)
                    {
                        return DecodePalettized(reader, width, height, textureFormat);
                    }
                    else if (pixelFormat == 4)
                    {
                        return DecodeBump(reader, width, height);
                    }
                    else if (pixelFormat == 6)
                    {
                        return DecodeYUV420(reader, width, height);
                    }
                    else if (pixelFormat == 3)
                    {
                        return DecodeYUV422(reader, width, height, textureFormat);
                    }
                    else if (textureFormat == 14 || textureFormat == 15)
                    {
                        return DecodeBMP(reader, width, height);
                    }
                    else if (pixelFormat == 0 || pixelFormat == 1 || pixelFormat == 2 || pixelFormat == 5 || pixelFormat == 7 || pixelFormat == 18)
                    {
                        return DecodeARGB(reader, width, height, pixelFormat, textureFormat);
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"不支持的解码格式:像素格式{pixelFormat},纹理格式{textureFormat}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码PVR时发生错误:{ex.Message}");
                return null;
            }
        }

        private Bitmap? DecodePalettized(BinaryReader reader, int width, int height, byte textureFormat)
        {
            try
            {
                int paletteEntries = (textureFormat == 7 || textureFormat == 8) ? 256 : 16;
                Color[] palette = new Color[paletteEntries];

                for (int i = 0; i < paletteEntries; i++)
                {
                    ushort color = reader.ReadUInt16();
                    Color pixel = ReadColor(color, 5);
                    palette[i] = pixel;
                }

                int totalPixels = width * height;
                byte[] indices = new byte[totalPixels];

                if (textureFormat == 7 || textureFormat == 8)
                {
                    for (int i = 0; i < totalPixels; i++)
                    {
                        indices[i] = reader.ReadByte();
                    }
                }
                else
                {
                    int bytesToRead = totalPixels / 2;
                    for (int i = 0; i < bytesToRead; i++)
                    {
                        byte b = reader.ReadByte();
                        indices[i * 2] = (byte)(b & 0x0F);
                        indices[i * 2 + 1] = (byte)((b >> 4) & 0x0F);
                    }
                }

                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = indices[y * width + x];
                        if (index < paletteEntries)
                        {
                            bitmap.SetPixel(x, y, palette[index]);
                        }
                        else
                        {
                            bitmap.SetPixel(x, y, Color.Magenta);
                        }
                    }
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码调色板图像失败:{ex.Message}");
                return null;
            }
        }

        private Bitmap? DecodeBump(BinaryReader reader, int width, int height)
        {
            try
            {
                int totalPixels = width * height;
                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                for (int i = 0; i < totalPixels; i++)
                {
                    ushort srValue = reader.ReadUInt16();
                    float S = (1.0f - ((srValue >> 8) / 255.0f)) * (float)Math.PI / 2.0f;
                    float R = (srValue & 0xFF) / 255.0f * 2.0f * (float)Math.PI - 2.0f * (float)Math.PI * ((srValue & 0xFF) > Math.PI ? 1 : 0);

                    float red = (float)(Math.Sin(S) * Math.Cos(R) + 1.0) * 0.5f;
                    float green = (float)(Math.Sin(S) * Math.Sin(R) + 1.0) * 0.5f;
                    float blue = (float)(Math.Cos(S) + 1.0) * 0.5f;

                    int r = (int)(red * 255);
                    int g = (int)(green * 255);
                    int b = (int)(blue * 255);

                    int x = i % width;
                    int y = i / width;
                    bitmap.SetPixel(x, y, Color.FromArgb(255, r, g, b));
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码BUMP图像失败:{ex.Message}");
                return null;
            }
        }

        private Bitmap? DecodeYUV420(BinaryReader reader, int width, int height)
        {
            try
            {
                int col = width / 16;
                int row = height / 16;

                List<byte[]> U = new List<byte[]>();
                List<byte[]> V = new List<byte[]>();
                List<byte[]> Y01 = new List<byte[]>();
                List<byte[]> Y23 = new List<byte[]>();

                for (int i = 0; i < 8 * row; i++)
                {
                    U.Add(new byte[col * 8]);
                    V.Add(new byte[col * 8]);
                }

                for (int i = 0; i < col; i++)
                {
                    Y01.Add(new byte[8]);
                    Y23.Add(new byte[8]);
                }

                for (int i = 0; i < row; i++)
                {
                    for (int n = 0; n < 8; n++)
                    {
                        reader.Read(U[n + 8 * i], 0, col * 8);
                    }

                    for (int n = 0; n < 8; n++)
                    {
                        reader.Read(V[n + 8 * i], 0, col * 8);
                    }

                    for (int k = 0; k < 2; k++)
                    {
                        for (int n = 0; n < 8; n++)
                        {
                            reader.Read(Y01[n], 0, 8);
                        }
                    }

                    for (int k = 0; k < 2; k++)
                    {
                        for (int n = 0; n < 8; n++)
                        {
                            reader.Read(Y23[n], 0, 8);
                        }
                    }
                }

                byte[] buffer = new byte[width * height * 3 / 2];
                int bufferPos = 0;

                for (int n = 0; n < col; n++)
                {
                    Array.Copy(Y01[n], 0, buffer, bufferPos, 8);
                    bufferPos += 8;
                }

                for (int n = 0; n < col; n++)
                {
                    Array.Copy(Y23[n], 0, buffer, bufferPos, 8);
                    bufferPos += 8;
                }

                for (int i = 0; i < U.Count; i++)
                {
                    Array.Copy(U[i], 0, buffer, bufferPos, U[i].Length);
                    bufferPos += U[i].Length;
                }

                for (int i = 0; i < V.Count; i++)
                {
                    Array.Copy(V[i], 0, buffer, bufferPos, V[i].Length);
                    bufferPos += V[i].Length;
                }

                byte[] Y = new byte[width * height];
                byte[] U_upsampled = new byte[width * height];
                byte[] V_upsampled = new byte[width * height];

                Array.Copy(buffer, 0, Y, 0, width * height);
                Array.Copy(buffer, width * height, U_upsampled, 0, width * height / 4);
                Array.Copy(buffer, width * height * 5 / 4, V_upsampled, 0, width * height / 4);

                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int yIndex = y * width + x;
                        int uvIndex = (y / 2) * (width / 2) + (x / 2);

                        byte yVal = Y[yIndex];
                        byte uVal = U_upsampled[uvIndex];
                        byte vVal = V_upsampled[uvIndex];

                        int r = (int)(yVal + 1.402 * (vVal - 128));
                        int g = (int)(yVal - 0.344136 * (uVal - 128) - 0.714136 * (vVal - 128));
                        int b = (int)(yVal + 1.772 * (uVal - 128));

                        r = Math.Max(0, Math.Min(255, r));
                        g = Math.Max(0, Math.Min(255, g));
                        b = Math.Max(0, Math.Min(255, b));

                        bitmap.SetPixel(x, y, Color.FromArgb(255, r, g, b));
                    }
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码YUV420图像失败:{ex.Message}");
                return null;
            }
        }

        private Bitmap? DecodeYUV422(BinaryReader reader, int width, int height, byte textureFormat)
        {
            try
            {
                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x += 2)
                    {
                        ushort yuv0 = reader.ReadUInt16();
                        ushort yuv1 = reader.ReadUInt16();

                        byte y0 = (byte)((yuv0 >> 8) & 0xFF);
                        byte u = (byte)(yuv0 & 0xFF);
                        byte y1 = (byte)((yuv1 >> 8) & 0xFF);
                        byte v = (byte)(yuv1 & 0xFF);

                        int c0 = y0 - 16;
                        int c1 = y1 - 16;
                        int d = u - 128;
                        int e = v - 128;

                        int r0 = (298 * c0 + 409 * e + 128) >> 8;
                        int g0 = (298 * c0 - 100 * d - 208 * e + 128) >> 8;
                        int b0 = (298 * c0 + 516 * d + 128) >> 8;

                        int r1 = (298 * c1 + 409 * e + 128) >> 8;
                        int g1 = (298 * c1 - 100 * d - 208 * e + 128) >> 8;
                        int b1 = (298 * c1 + 516 * d + 128) >> 8;

                        r0 = Math.Max(0, Math.Min(255, r0));
                        g0 = Math.Max(0, Math.Min(255, g0));
                        b0 = Math.Max(0, Math.Min(255, b0));

                        r1 = Math.Max(0, Math.Min(255, r1));
                        g1 = Math.Max(0, Math.Min(255, g1));
                        b1 = Math.Max(0, Math.Min(255, b1));

                        bitmap.SetPixel(x, y, Color.FromArgb(255, r0, g0, b0));
                        if (x + 1 < width)
                        {
                            bitmap.SetPixel(x + 1, y, Color.FromArgb(255, r1, g1, b1));
                        }
                    }
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码YUV422图像失败:{ex.Message}");
                return null;
            }
        }

        private Bitmap? DecodeBMP(BinaryReader reader, int width, int height)
        {
            try
            {
                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                for (int i = 0; i < width * height; i++)
                {
                    uint color = reader.ReadUInt32();
                    byte r = (byte)((color >> 24) & 0xFF);
                    byte g = (byte)((color >> 16) & 0xFF);
                    byte b = (byte)((color >> 8) & 0xFF);
                    byte a = (byte)(color & 0xFF);

                    int x = i % width;
                    int y = i / width;
                    bitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码BMP图像失败:{ex.Message}");
                return null;
            }
        }

        private Bitmap? DecodeARGB(BinaryReader reader, int width, int height, byte pixelFormat, byte textureFormat)
        {
            try
            {
                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                for (int i = 0; i < width * height; i++)
                {
                    ushort color = reader.ReadUInt16();
                    Color pixel = ReadColor(color, pixelFormat);

                    int x = i % width;
                    int y = i / width;
                    bitmap.SetPixel(x, y, pixel);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解码ARGB图像失败:{ex.Message}");
                return null;
            }
        }

        private Color ReadColor(ushort color, byte pixelFormat)
        {
            switch (pixelFormat)
            {
                case 0:
                    byte a1555 = (byte)(((color >> 15) & 0x1) * 0xFF);
                    byte r1555 = (byte)(((color >> 10) & 0x1F) * 0xFF / 0x1F);
                    byte g1555 = (byte)(((color >> 5) & 0x1F) * 0xFF / 0x1F);
                    byte b1555 = (byte)((color & 0x1F) * 0xFF / 0x1F);
                    return Color.FromArgb(a1555, r1555, g1555, b1555);

                case 1:
                    byte r565 = (byte)(((color >> 11) & 0x1F) * 0xFF / 0x1F);
                    byte g565 = (byte)(((color >> 5) & 0x3F) * 0xFF / 0x3F);
                    byte b565 = (byte)((color & 0x1F) * 0xFF / 0x1F);
                    return Color.FromArgb(0xFF, r565, g565, b565);

                case 2:
                    byte a4444 = (byte)(((color >> 12) & 0xF) * 0x11);
                    byte r4444 = (byte)(((color >> 8) & 0xF) * 0x11);
                    byte g4444 = (byte)(((color >> 4) & 0xF) * 0x11);
                    byte b4444 = (byte)((color & 0xF) * 0x11);
                    return Color.FromArgb(a4444, r4444, g4444, b4444);

                case 5:
                    byte r555 = (byte)(((color >> 10) & 0x1F) * 0xFF / 0x1F);
                    byte g555 = (byte)(((color >> 5) & 0x1F) * 0xFF / 0x1F);
                    byte b555 = (byte)((color & 0x1F) * 0xFF / 0x1F);
                    return Color.FromArgb(0xFF, r555, g555, b555);

                case 7:
                default:
                    return Color.FromArgb(0xFF, 0xFF, 0, 0);
            }
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