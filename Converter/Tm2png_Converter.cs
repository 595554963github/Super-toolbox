using System.Drawing.Imaging;

namespace super_toolbox
{
    public class Tm2png_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private struct RGBAColor
        {
            public byte R;
            public byte G;
            public byte B;
            public byte A;

            public byte[] ToBytes()
            {
                return new byte[] { R, G, B, A };
            }

            public bool Match(RGBAColor color)
            {
                return R == color.R && G == color.G && B == color.B && A == color.A;
            }

            public bool Compare(RGBAColor original, RGBAColor other)
            {
                var a = new int[] { R, G, B, A };
                var b = new int[] { other.R, other.G, other.B, other.A };
                var c = new int[] { original.R, original.G, original.B, original.A };
                return EuclideanDistance(a, c) <= EuclideanDistance(b, c);
            }

            public uint ToUInt()
            {
                return (uint)((A << 24) | (B << 16) | (G << 8) | R);
            }

            private static double EuclideanDistance(int[] a, int[] b)
            {
                double sum = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    sum += Math.Pow(a[i] - b[i], 2);
                }
                return Math.Sqrt(sum);
            }
        }

        private struct TM2Image
        {
            public byte[] Magic;
            public byte[] Unk;
            public int DataLen;
            public int PaletteLen;
            public int ImgDataLen;
            public short HeaderLen;
            public short ColorCount;
            public byte ImgFormat;
            public byte MipmapCount;
            public byte CLUTFormat;
            public byte Bpp;
            public short ImgWidth;
            public short ImgHeight;
            public byte[] GsTEX0;
            public byte[] GsTEX1;
            public byte[] GsRegs;
            public byte[] GsTexClut;
            public byte[] ImgData;
            public List<RGBAColor> Palettes;
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
            var tm2Files = Directory.EnumerateFiles(directoryPath, "*.tm2", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directoryPath, "*.TM2", SearchOption.AllDirectories))
                .Distinct()
                .ToArray();

            TotalFilesToConvert = tm2Files.Length;
            int successCount = 0;
            int totalImages = 0;

            try
            {
                foreach (var tm2FilePath in tm2Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(tm2FilePath)}");

                    try
                    {
                        int converted = await ConvertTm2ToPng(tm2FilePath, cancellationToken);

                        if (converted > 0)
                        {
                            successCount++;
                            totalImages += converted;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(tm2FilePath)} -> {converted}个图像");
                            OnFileConverted(tm2FilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{Path.GetFileName(tm2FilePath)}转换失败");
                            OnConversionFailed($"{Path.GetFileName(tm2FilePath)}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{Path.GetFileName(tm2FilePath)}处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件,共{totalImages}个图像");
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

        private async Task<int> ConvertTm2ToPng(string tm2FilePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var stream = File.OpenRead(tm2FilePath))
                    using (var br = new BinaryReader(stream))
                    {
                        var images = new List<TM2Image>();

                        while (br.BaseStream.Position < br.BaseStream.Length)
                        {
                            var img = new TM2Image();

                            img.Magic = br.ReadBytes(4);
                            if (img.Magic[0] != 0x54 || img.Magic[1] != 0x49 ||
                                img.Magic[2] != 0x4D || img.Magic[3] != 0x32)
                                break;

                            img.Unk = br.ReadBytes(0xC);
                            img.DataLen = br.ReadInt32();
                            img.PaletteLen = br.ReadInt32();
                            img.ImgDataLen = br.ReadInt32();
                            img.HeaderLen = br.ReadInt16();
                            img.ColorCount = br.ReadInt16();
                            img.ImgFormat = br.ReadByte();
                            img.MipmapCount = br.ReadByte();
                            img.CLUTFormat = br.ReadByte();
                            img.Bpp = br.ReadByte();
                            img.ImgWidth = br.ReadInt16();
                            img.ImgHeight = br.ReadInt16();
                            img.GsTEX0 = br.ReadBytes(8);
                            img.GsTEX1 = br.ReadBytes(8);
                            img.GsRegs = br.ReadBytes(4);
                            img.GsTexClut = br.ReadBytes(4);

                            img.ImgData = br.ReadBytes(img.ImgDataLen);

                            img.Palettes = new List<RGBAColor>();

                            if (img.Bpp == 4)
                            {
                                for (int i = 0; i < img.ColorCount; i++)
                                {
                                    var color = new RGBAColor();
                                    color.R = br.ReadByte();
                                    color.G = br.ReadByte();
                                    color.B = br.ReadByte();
                                    color.A = br.ReadByte();
                                    img.Palettes.Add(color);
                                }
                            }
                            else if (img.Bpp == 5)
                            {
                                byte[] originalData = br.ReadBytes(img.PaletteLen);
                                List<byte> reved = new List<byte>();
                                int parts = img.PaletteLen / 128;
                                int stripes = 2;
                                int colors = 32;
                                int blocks = 2;

                                for (int part = 0; part < parts; part++)
                                {
                                    for (int block = 0; block < blocks; block++)
                                    {
                                        for (int stripe = 0; stripe < stripes; stripe++)
                                        {
                                            for (int color = 0; color < colors; color++)
                                            {
                                                reved.Add(originalData[part * colors * stripes * blocks + block * colors + stripe * stripes * colors + color]);
                                            }
                                        }
                                    }
                                }

                                using (var ms = new MemoryStream(reved.ToArray()))
                                using (var brms = new BinaryReader(ms))
                                {
                                    for (int i = 0; i < img.ColorCount; i++)
                                    {
                                        var color = new RGBAColor();
                                        color.R = brms.ReadByte();
                                        color.G = brms.ReadByte();
                                        color.B = brms.ReadByte();
                                        color.A = brms.ReadByte();
                                        img.Palettes.Add(color);
                                    }
                                }
                            }

                            images.Add(img);
                        }

                        string fileDir = Path.GetDirectoryName(tm2FilePath) ?? string.Empty;
                        string baseFileName = Path.GetFileNameWithoutExtension(tm2FilePath);
                        int convertedCount = 0;

                        for (int imgIdx = 0; imgIdx < images.Count; imgIdx++)
                        {
                            var img = images[imgIdx];

                            string outputPath;
                            if (images.Count == 1)
                                outputPath = Path.Combine(fileDir, baseFileName + ".png");
                            else
                                outputPath = Path.Combine(fileDir, $"{baseFileName}_{imgIdx:D4}.png");

                            using (var bitmap = CreateBitmapFromTM2Image(img))
                            {
                                if (bitmap != null)
                                {
                                    bitmap.Save(outputPath, ImageFormat.Png);
                                    convertedCount++;
                                }
                            }
                        }

                        return convertedCount;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"转换错误:{ex.Message}");
                    return 0;
                }
            }, cancellationToken);
        }

        private Bitmap? CreateBitmapFromTM2Image(TM2Image img)
        {
            try
            {
                int width = img.ImgWidth;
                int height = img.ImgHeight;

                if (img.Bpp == 4)
                {
                    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    int idx = 0;

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x += 2)
                        {
                            if (idx >= img.ImgData.Length) continue;

                            byte b = img.ImgData[idx];
                            int low = b & 0x0F;
                            int high = b >> 4;

                            if (x < width && high < img.Palettes.Count)
                            {
                                var color = img.Palettes[high];
                                bitmap.SetPixel(x, y, Color.FromArgb(color.A, color.R, color.G, color.B));
                            }

                            if (x + 1 < width && low < img.Palettes.Count)
                            {
                                var color = img.Palettes[low];
                                bitmap.SetPixel(x + 1, y, Color.FromArgb(color.A, color.R, color.G, color.B));
                            }

                            idx++;
                        }
                    }
                    return bitmap;
                }
                else if (img.Bpp == 5)
                {
                    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int idx = y * width + x;
                            if (idx >= img.ImgData.Length) continue;

                            int palIdx = img.ImgData[idx];
                            if (palIdx < img.Palettes.Count)
                            {
                                var color = img.Palettes[palIdx];
                                bitmap.SetPixel(x, y, Color.FromArgb(color.A, color.R, color.G, color.B));
                            }
                        }
                    }
                    return bitmap;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public bool ConvertSingleFile(string inputPath, string? outputPath = null)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    ConversionError?.Invoke(this, $"文件不存在:{inputPath}");
                    return false;
                }

                byte[] fileData = File.ReadAllBytes(inputPath);

                using (var stream = new MemoryStream(fileData))
                using (var br = new BinaryReader(stream))
                {
                    var images = new List<TM2Image>();

                    while (br.BaseStream.Position < br.BaseStream.Length)
                    {
                        var img = new TM2Image();

                        img.Magic = br.ReadBytes(4);
                        if (img.Magic[0] != 0x54 || img.Magic[1] != 0x49 ||
                            img.Magic[2] != 0x4D || img.Magic[3] != 0x32)
                            break;

                        img.Unk = br.ReadBytes(0xC);
                        img.DataLen = br.ReadInt32();
                        img.PaletteLen = br.ReadInt32();
                        img.ImgDataLen = br.ReadInt32();
                        img.HeaderLen = br.ReadInt16();
                        img.ColorCount = br.ReadInt16();
                        img.ImgFormat = br.ReadByte();
                        img.MipmapCount = br.ReadByte();
                        img.CLUTFormat = br.ReadByte();
                        img.Bpp = br.ReadByte();
                        img.ImgWidth = br.ReadInt16();
                        img.ImgHeight = br.ReadInt16();
                        img.GsTEX0 = br.ReadBytes(8);
                        img.GsTEX1 = br.ReadBytes(8);
                        img.GsRegs = br.ReadBytes(4);
                        img.GsTexClut = br.ReadBytes(4);

                        img.ImgData = br.ReadBytes(img.ImgDataLen);

                        img.Palettes = new List<RGBAColor>();

                        if (img.Bpp == 4)
                        {
                            for (int i = 0; i < img.ColorCount; i++)
                            {
                                var color = new RGBAColor();
                                color.R = br.ReadByte();
                                color.G = br.ReadByte();
                                color.B = br.ReadByte();
                                color.A = br.ReadByte();
                                img.Palettes.Add(color);
                            }
                        }
                        else if (img.Bpp == 5)
                        {
                            byte[] originalData = br.ReadBytes(img.PaletteLen);
                            List<byte> reved = new List<byte>();
                            int parts = img.PaletteLen / 128;
                            int stripes = 2;
                            int colors = 32;
                            int blocks = 2;

                            for (int part = 0; part < parts; part++)
                            {
                                for (int block = 0; block < blocks; block++)
                                {
                                    for (int stripe = 0; stripe < stripes; stripe++)
                                    {
                                        for (int color = 0; color < colors; color++)
                                        {
                                            reved.Add(originalData[part * colors * stripes * blocks + block * colors + stripe * stripes * colors + color]);
                                        }
                                    }
                                }
                            }

                            using (var ms = new MemoryStream(reved.ToArray()))
                            using (var brms = new BinaryReader(ms))
                            {
                                for (int i = 0; i < img.ColorCount; i++)
                                {
                                    var color = new RGBAColor();
                                    color.R = brms.ReadByte();
                                    color.G = brms.ReadByte();
                                    color.B = brms.ReadByte();
                                    color.A = brms.ReadByte();
                                    img.Palettes.Add(color);
                                }
                            }
                        }

                        images.Add(img);
                    }

                    string fileDir = Path.GetDirectoryName(inputPath) ?? string.Empty;
                    string baseFileName = Path.GetFileNameWithoutExtension(inputPath);

                    for (int imgIdx = 0; imgIdx < images.Count; imgIdx++)
                    {
                        var img = images[imgIdx];

                        string outputFilePath;
                        if (outputPath != null)
                            outputFilePath = outputPath;
                        else
                            outputFilePath = Path.Combine(fileDir, baseFileName + ".png");

                        using (var bitmap = CreateBitmapFromTM2Image(img))
                        {
                            if (bitmap != null)
                            {
                                bitmap.Save(outputFilePath, ImageFormat.Png);
                            }
                        }
                    }

                    ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(inputPath)} -> {images.Count}个图像");
                    OnFileConverted(inputPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换失败:{ex.Message}");
                return false;
            }
        }

        public void ConvertMultipleFiles(string[] inputPaths, string? outputDirectory = null)
        {
            if (inputPaths == null || inputPaths.Length == 0)
            {
                ConversionError?.Invoke(this, "没有指定要转换的文件");
                return;
            }

            TotalFilesToConvert = inputPaths.Length;
            int successCount = 0;

            ConversionStarted?.Invoke(this, $"开始批量转换{inputPaths.Length}个文件");

            foreach (var inputPath in inputPaths)
            {
                try
                {
                    if (ConvertSingleFile(inputPath, null))
                    {
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    ConversionError?.Invoke(this, $"文件转换异常:{Path.GetFileName(inputPath)} - {ex.Message}");
                }
            }

            ConversionProgress?.Invoke(this, $"批量转换完成,成功{successCount}/{TotalFilesToConvert}个文件");
            OnConversionCompleted();
        }
    }
}