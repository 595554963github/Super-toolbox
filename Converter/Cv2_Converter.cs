using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Cv2_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var cv2Files = Directory.GetFiles(directoryPath, "*.cv2", SearchOption.AllDirectories).ToArray();

            TotalFilesToConvert = cv2Files.Length;
            int successCount = 0;

            try
            {
                foreach (var cv2FilePath in cv2Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(cv2FilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}");

                    try
                    {
                        string outputFile = await ConvertCv2ToPng(cv2FilePath, cancellationToken);

                        if (!string.IsNullOrEmpty(outputFile) && File.Exists(outputFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(outputFile)}");
                            OnFileConverted(outputFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}转换失败");
                            OnConversionFailed($"{fileName}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}处理错误:{ex.Message}");
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

        private async Task<string> ConvertCv2ToPng(string cv2FilePath, CancellationToken cancellationToken)
        {
            await Task.Yield(); 

            try
            {
                using (FileStream fs = new FileStream(cv2FilePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 17)
                        throw new IOException("CV2文件太小");

                    CV2Header header = CV2Header.Deserialize(fs);

                    if (header.Height * header.PaddedWidth * header.Bpp > fs.Length - 17)
                        throw new IOException("CV2文件大小与数据不匹配");

                    fs.Seek(17, SeekOrigin.Begin);
                    Bitmap image = new Bitmap(header.Width, header.Height, header.PixelFormat);

                    int lineSize = header.Width * header.Bpp;
                    byte[] lineBuf = new byte[header.PaddedWidth * header.Bpp];

                    for (int y = 0; y < header.Height; ++y)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        fs.Read(lineBuf, 0, lineBuf.Length);
                        BitmapData data = image.LockBits(
                            new Rectangle(0, y, header.Width, 1),
                            ImageLockMode.WriteOnly, header.PixelFormat);
                        Marshal.Copy(lineBuf, 0, data.Scan0, lineSize);
                        image.UnlockBits(data);
                    }

                    if (header.bpp == 8)
                    {
                        string paletteName = Path.Combine(
                            Path.GetDirectoryName(cv2FilePath) ?? string.Empty,
                            "palette000.pal");

                        if (File.Exists(paletteName))
                        {
                            Color[] newPalette = LoadPalette(paletteName);
                            ColorPalette palette = image.Palette;
                            Array.Copy(newPalette, palette.Entries, 256);
                            image.Palette = palette;
                        }
                    }

                    string outputFileName = Path.ChangeExtension(cv2FilePath, ".png");
                    image.Save(outputFileName, ImageFormat.Png);
                    image.Dispose();

                    return outputFileName;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return string.Empty;
            }
        }

        private Color[] LoadPalette(string paletteName)
        {
            using (FileStream pal = new FileStream(paletteName, FileMode.Open, FileAccess.Read))
            {
                if (pal.Length < 1)
                    throw new IOException("调色板文件太小");

                int bpp = pal.ReadByte();
                int expectedSize = (bpp >> 3) * 256 + 1;

                if (pal.Length != expectedSize)
                    throw new IOException("调色板文件大小不正确");

                Color[] palette = new Color[256];
                byte[] palBuf = new byte[bpp >> 3];

                for (int i = 0; i < 256; ++i)
                {
                    pal.Read(palBuf, 0, palBuf.Length);
                    palette[i] = ConvertColor(bpp, palBuf);
                }

                return palette;
            }
        }

        private Color ConvertColor(int bpp, byte[] src)
        {
            switch (bpp)
            {
                case 16:
                    return ColourConverter.FromBGRA5551(src);
                case 24:
                    return ColourConverter.FromBGR888(src);
                case 32:
                    return ColourConverter.FromBGRA8888(src);
                default:
                    throw new FormatException($"不支持的色深度:{bpp}");
            }
        }
    }
    public class CV2Header
    {
        private static readonly Dictionary<int, PixelFormat> pixelFormatTable = new Dictionary<int, PixelFormat>
        {
            { 8, PixelFormat.Format8bppIndexed },
            { 16, PixelFormat.Format16bppRgb565 },
            { 24, PixelFormat.Format32bppArgb },
            { 32, PixelFormat.Format32bppArgb }
        };

        private static readonly Dictionary<int, int> bppTable = new Dictionary<int, int>
        {
            { 8, 1 },
            { 16, 2 },
            { 24, 4 },
            { 32, 4 }
        };

        private int _bpp;
        public int bpp
        {
            get { return _bpp; }
            set
            {
                if (!pixelFormatTable.ContainsKey(value))
                    throw new FormatException($"不支持的色深度:{value}");
                _bpp = value;
                _pixelFormat = pixelFormatTable[value];
            }
        }

        public int Bpp => bppTable[_bpp];

        private PixelFormat _pixelFormat;
        public PixelFormat PixelFormat => _pixelFormat;

        public int Width { get; set; }
        public int Height { get; set; }
        public int PaddedWidth { get; set; }

        public static CV2Header Deserialize(Stream input)
        {
            BinaryReaderEx reader = new BinaryReaderEx(input);
            CV2Header header = new CV2Header();

            header.bpp = (int)reader.ReadByte();
            header.Width = reader.ReadInt32();
            header.Height = reader.ReadInt32();
            header.PaddedWidth = reader.ReadInt32();

            int reserved = reader.ReadInt32();

            if (header.PaddedWidth < header.Width)
                throw new FormatException("CV2头部数据无效");

            return header;
        }
    }

    public static class ColourConverter
    {
        private static readonly int[] Color5Bits = {
            0, 8, 16, 24, 32, 41, 49, 57,
            65, 74, 82, 90, 98, 106, 115, 123,
            131, 139, 148, 156, 164, 172, 180, 189,
            197, 205, 213, 222, 230, 238, 246, 255
        };

        public static Color FromBGRA5551(byte[] src)
        {
            int A = (src[1] & 0x80) > 0 ? 0xFF : 0x00;
            int R = (src[1] & 0x7C) >> 2;
            int G = ((src[1] & 0x03) << 3) | ((src[0] & 0xE0) >> 5);
            int B = src[0] & 0x1F;
            return Color.FromArgb(A, Color5Bits[R], Color5Bits[G], Color5Bits[B]);
        }

        public static Color FromBGR888(byte[] src)
        {
            return Color.FromArgb(255, src[2], src[1], src[0]);
        }

        public static Color FromBGRA8888(byte[] src)
        {
            return Color.FromArgb(src[3], src[2], src[1], src[0]);
        }
    }
    public enum Endian
    {
        Big,
        Little
    }

    public class BinaryReaderEx
    {
        private readonly Endian machineEndian = BitConverter.IsLittleEndian ? Endian.Little : Endian.Big;
        public Stream BaseStream { get; }
        public Endian endian = Endian.Little;

        public BinaryReaderEx(Stream input)
        {
            BaseStream = input;
        }

        public byte ReadByte()
        {
            int value = BaseStream.ReadByte();
            if (value == -1)
                throw new EndOfStreamException();
            return (byte)value;
        }

        public int ReadInt32()
        {
            return BitConverter.ToInt32(ReadBytesInternal(4), 0);
        }

        private byte[] ReadBytesInternal(int count)
        {
            byte[] buff = new byte[count];
            int bytesRead = BaseStream.Read(buff, 0, count);

            if (bytesRead != count)
                throw new EndOfStreamException();

            if (machineEndian != endian)
            {
                Array.Reverse(buff);
            }

            return buff;
        }
    }
}