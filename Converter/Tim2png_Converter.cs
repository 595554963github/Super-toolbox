using System.Drawing.Imaging;

namespace super_toolbox
{
    public class Tim2png_Converter : BaseExtractor
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

            var timFiles = Directory.EnumerateFiles(directoryPath, "*.tim", SearchOption.AllDirectories).ToList();

            TotalFilesToConvert = timFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var filePath in timFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    try
                    {
                        bool conversionSuccess = await ConvertTimToPngAsync(filePath, pngFilePath, cancellationToken);

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

        private async Task<bool> ConvertTimToPngAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] fileBuf = await File.ReadAllBytesAsync(inputPath, cancellationToken);

                if (fileBuf.Length < 8)
                {
                    ConversionError?.Invoke(this, "文件太小,不是有效的TIM文件");
                    return false;
                }

                Bitmap? bitmap = ReadTimImage(fileBuf);

                if (bitmap == null)
                {
                    ConversionError?.Invoke(this, "创建位图失败");
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

        private Bitmap? ReadTimImage(byte[] data)
        {
            try
            {
                if (data.Length < 8) return null;

                if (data[0] != 0x10 || data[1] != 0x00 || data[2] != 0x00 || data[3] != 0x00)
                {
                    ConversionError?.Invoke(this, "不是有效的TIM文件");
                    return null;
                }

                int flags = BitConverter.ToInt32(data, 4);
                int pMode = flags & 7;

                if (pMode > 4)
                {
                    ConversionError?.Invoke(this, "不是有效的TIM文件");
                    return null;
                }

                int offset = 8;
                Color[]? paletteColors = null;
                bool haveClut = (flags & 8) != 0;

                if (haveClut)
                {
                    int clutSize = BitConverter.ToInt32(data, offset);
                    offset += 4;

                    if (clutSize < 12)
                    {
                        ConversionError?.Invoke(this, "CLUT数据太小");
                        return null;
                    }

                    int numEntries = (clutSize - 12) / 2;
                    offset += 8;

                    byte[] clutData = new byte[numEntries * 2];
                    Array.Copy(data, offset, clutData, 0, numEntries * 2);
                    offset += numEntries * 2;

                    byte[] convertedClut = ConvertABGR(clutData);

                    int paletteSize = (pMode == 0) ? 16 : 256;
                    paletteColors = new Color[paletteSize];

                    for (int i = 0; i < paletteSize; i++)
                    {
                        if (i < convertedClut.Length / 2)
                        {
                            ushort color = BitConverter.ToUInt16(convertedClut, i * 2);
                            int r = ((color >> 10) & 0x1F) << 3;
                            int g = ((color >> 5) & 0x1F) << 3;
                            int b = (color & 0x1F) << 3;
                            r |= r >> 5;
                            g |= g >> 5;
                            b |= b >> 5;
                            paletteColors[i] = Color.FromArgb(255, r, g, b);
                        }
                        else
                        {
                            paletteColors[i] = Color.FromArgb(0, 0, 0, 0);
                        }
                    }
                }

                int dataSize = BitConverter.ToInt32(data, offset);
                offset += 4;

                if (dataSize < 12)
                {
                    ConversionError?.Invoke(this, "像素数据太小");
                    return null;
                }

                offset += 4;

                int width = BitConverter.ToUInt16(data, offset);
                int height = BitConverter.ToUInt16(data, offset + 2);
                offset += 4;

                int expectedSize = width * height * 2;
                byte[] pixelData = new byte[expectedSize];
                Array.Copy(data, offset, pixelData, 0, Math.Min(expectedSize, data.Length - offset));

                Bitmap? bitmap = null;

                if (pMode == 0)
                {
                    width *= 4;
                    byte[] indexedData = new byte[width * height];
                    int idx = 0;
                    for (int i = 0; i < pixelData.Length; i++)
                    {
                        indexedData[idx++] = (byte)(pixelData[i] & 0x0F);
                        indexedData[idx++] = (byte)(pixelData[i] >> 4);
                    }

                    bitmap = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
                    ColorPalette pal = bitmap.Palette;
                    if (paletteColors != null)
                    {
                        for (int i = 0; i < 16 && i < paletteColors.Length; i++)
                        {
                            pal.Entries[i] = paletteColors[i];
                        }
                        bitmap.Palette = pal;
                    }

                    BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                    System.Runtime.InteropServices.Marshal.Copy(indexedData, 0, bmpData.Scan0, indexedData.Length);
                    bitmap.UnlockBits(bmpData);
                }
                else if (pMode == 1)
                {
                    width *= 2;
                    bitmap = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
                    ColorPalette pal = bitmap.Palette;
                    if (paletteColors != null)
                    {
                        for (int i = 0; i < 256 && i < paletteColors.Length; i++)
                        {
                            pal.Entries[i] = paletteColors[i];
                        }
                        bitmap.Palette = pal;
                    }

                    BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                    System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, bmpData.Scan0, pixelData.Length);
                    bitmap.UnlockBits(bmpData);
                }
                else if (pMode == 2)
                {
                    byte[] output = new byte[width * height * 2];
                    for (int i = 0; i < pixelData.Length; i += 2)
                    {
                        ushort pixel = BitConverter.ToUInt16(pixelData, i);
                        int r = (pixel & 0x1F) << 3;
                        int g = ((pixel >> 5) & 0x1F) << 3;
                        int b = ((pixel >> 10) & 0x1F) << 3;
                        r |= r >> 5;
                        g |= g >> 5;
                        b |= b >> 5;
                        ushort rgb555 = (ushort)((r >> 3) | ((g >> 3) << 5) | ((b >> 3) << 10));
                        byte[] bytes = BitConverter.GetBytes(rgb555);
                        output[i] = bytes[0];
                        output[i + 1] = bytes[1];
                    }

                    bitmap = new Bitmap(width, height, PixelFormat.Format16bppRgb555);
                    BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format16bppRgb555);
                    System.Runtime.InteropServices.Marshal.Copy(output, 0, bmpData.Scan0, output.Length);
                    bitmap.UnlockBits(bmpData);
                }
                else if (pMode == 3)
                {
                    width = width * 2 / 3;
                    bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                    BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                    System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, bmpData.Scan0, pixelData.Length);
                    bitmap.UnlockBits(bmpData);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解析TIM文件失败:{ex.Message}");
                return null;
            }
        }

        private byte[] ConvertABGR(byte[] data)
        {
            byte[] output = new byte[data.Length];
            for (int i = 0; i < data.Length; i += 2)
            {
                ushort pixel = BitConverter.ToUInt16(data, i);
                int r = pixel & 0x1F;
                int g = (pixel >> 5) & 0x1F;
                int b = (pixel >> 10) & 0x1F;
                int a = pixel & 0x8000;
                ushort rgb555 = (ushort)(a | (r << 10) | (g << 5) | b);
                byte[] bytes = BitConverter.GetBytes(rgb555);
                output[i] = bytes[0];
                output[i + 1] = bytes[1];
            }
            return output;
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
