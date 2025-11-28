using System.Drawing.Imaging;

namespace super_toolbox
{
    public class Hip2Png_Converter : BaseExtractor
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
            var hipFiles = Directory.EnumerateFiles(directoryPath, "*.hip", SearchOption.AllDirectories).ToList();
            var hplFiles = Directory.EnumerateFiles(directoryPath, "*.hpl", SearchOption.AllDirectories).ToList();
            var allFiles = hipFiles.Concat(hplFiles).ToList();
            TotalFilesToConvert = allFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var filePath in allFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");
                    try
                    {
                        bool conversionSuccess = await ConvertHipHplToPngAsync(filePath, pngFilePath, cancellationToken);
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
        private async Task<bool> ConvertHipHplToPngAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] fileBuf = await File.ReadAllBytesAsync(inputPath, cancellationToken);

                if (fileBuf.Length < 4)
                {
                    ConversionError?.Invoke(this, "文件太小，不是有效的HIP/HPL文件");
                    return false;
                }
                byte[] magic = new byte[4];
                Array.Copy(fileBuf, 0, magic, 0, 4);
                Bitmap? bitmap = null;
                if (magic.SequenceEqual(new byte[] { (byte)'H', (byte)'I', (byte)'P', 0 }))
                {
                    ConversionProgress?.Invoke(this, "检测到HIP文件");
                    bitmap = await ConvertHipToBitmapAsync(fileBuf, cancellationToken);
                }
                else if (magic.SequenceEqual(new byte[] { (byte)'H', (byte)'P', (byte)'A', (byte)'L' }))
                {
                    ConversionProgress?.Invoke(this, "检测到HPL文件(调色板)");
                    bitmap = await ConvertHplToBitmapAsync(fileBuf, cancellationToken);
                }
                else
                {
                    ConversionError?.Invoke(this, $"不支持的文件格式，文件头:{BitConverter.ToString(magic)}");
                    return false;
                }
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
        private async Task<Bitmap?> ConvertHipToBitmapAsync(byte[] fileData, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Yield();

                if (fileData.Length < 0x20)
                {
                    ConversionError?.Invoke(this, "HIP文件太小");
                    return null;
                }
                int offset = 0;
                offset += 4;
                uint version = BitConverter.ToUInt32(fileData, offset);
                offset += 4;
                offset += 4;
                uint paletteSize = BitConverter.ToUInt32(fileData, offset);
                offset += 4;
                uint textureW = BitConverter.ToUInt32(fileData, offset);
                offset += 4;
                uint textureH = BitConverter.ToUInt32(fileData, offset);
                offset += 4;
                byte encodingTag = fileData[offset];
                offset += 1;
                offset += 3;
                uint extraHeaderSize = BitConverter.ToUInt32(fileData, offset);
                offset += 4;
                uint imageWidth = textureW;
                uint imageHeight = textureH;
                if (extraHeaderSize >= 0x10)
                {
                    uint width = BitConverter.ToUInt32(fileData, offset);
                    offset += 4;
                    uint height = BitConverter.ToUInt32(fileData, offset);
                    offset += 4;
                    imageWidth = width;
                    imageHeight = height;
                    offset += 8;
                    offset += (int)(extraHeaderSize - 0x10);
                }
                if (imageWidth == 0 || imageHeight == 0)
                {
                    ConversionError?.Invoke(this, $"无效的图像尺寸:{imageWidth}x{imageHeight}");
                    return null;
                }

                if (imageWidth > 8192 || imageHeight > 8192)
                {
                    ConversionError?.Invoke(this, $"图像尺寸过大:{imageWidth}x{imageHeight}");
                    return null;
                }

                ConversionProgress?.Invoke(this, $"图像尺寸:{imageWidth}x{imageHeight}, 编码类型:0x{encodingTag:X2}");
                switch (encodingTag)
                {
                    case 0x01:
                        return CreateIndexedBitmap(fileData, offset, paletteSize, (int)imageWidth, (int)imageHeight);
                    case 0x10:
                        return CreateArgbBitmap(fileData, offset, (int)imageWidth, (int)imageHeight);
                    case 0x04:
                        return CreateLumaBitmap(fileData, offset, (int)imageWidth, (int)imageHeight);
                    default:
                        ConversionError?.Invoke(this, $"不支持的编码格式:0x{encodingTag:X2}");
                        return null;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解析HIP文件失败:{ex.Message}");
                return null;
            }
        }
        private async Task<Bitmap?> ConvertHplToBitmapAsync(byte[] fileData, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Yield();
                if (fileData.Length < 0x20)
                {
                    ConversionError?.Invoke(this, "HPL文件太小");
                    return null;
                }
                int offset = 0;
                offset += 4;
                offset += 8;
                uint paletteSize = BitConverter.ToUInt32(fileData, offset);
                offset += 4;
                offset += 12;
                offset += 4;
                List<Color> palette = new List<Color>();
                for (int i = 0; i < paletteSize; i++)
                {
                    if (offset + 4 > fileData.Length)
                    {
                        ConversionError?.Invoke(this, "调色板数据不完整");
                        return null;
                    }
                    byte b = fileData[offset];
                    byte g = fileData[offset + 1];
                    byte r = fileData[offset + 2];
                    byte a = fileData[offset + 3];
                    palette.Add(Color.FromArgb(a, r, g, b));
                    offset += 4;
                }

                ConversionProgress?.Invoke(this, $"调色板大小:{palette.Count}种颜色");
                int width = palette.Count;
                int height = 1;
                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                for (int x = 0; x < width; x++)
                {
                    bitmap.SetPixel(x, 0, palette[x]);
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"解析HPL文件失败:{ex.Message}");
                return null;
            }
        }
        private Bitmap? CreateIndexedBitmap(byte[] fileData, int dataOffset, uint paletteSize, int width, int height)
        {
            try
            {
                List<Color> palette = new List<Color>();
                for (int i = 0; i < paletteSize; i++)
                {
                    if (dataOffset + 4 > fileData.Length)
                    {
                        ConversionError?.Invoke(this, "调色板数据不完整");
                        return null;
                    }
                    byte b = fileData[dataOffset];
                    byte g = fileData[dataOffset + 1];
                    byte r = fileData[dataOffset + 2];
                    byte a = fileData[dataOffset + 3];
                    palette.Add(Color.FromArgb(a, r, g, b));
                    dataOffset += 4;
                }

                List<byte> indices = new List<byte>();
                int expectedPixels = width * height;

                while (indices.Count < expectedPixels)
                {
                    if (dataOffset + 2 > fileData.Length)
                    {
                        ConversionError?.Invoke(this, "索引数据不完整");
                        return null;
                    }
                    byte index = fileData[dataOffset];
                    byte runLength = fileData[dataOffset + 1];
                    dataOffset += 2;

                    int remainingPixels = expectedPixels - indices.Count;
                    int actualRunLength = Math.Min(runLength, remainingPixels);

                    for (int i = 0; i < actualRunLength; i++)
                    {
                        indices.Add(index);
                    }

                    if (indices.Count >= expectedPixels)
                        break;
                }
                if (indices.Count != expectedPixels)
                {
                    ConversionProgress?.Invoke(this, $"警告: 索引数量({indices.Count})与预期像素数({expectedPixels})不匹配");
                }

                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                for (int i = 0; i < indices.Count && i < expectedPixels; i++)
                {
                    int x = i % width;
                    int y = i / width;
                    if (x < 0 || x >= width || y < 0 || y >= height)
                    {
                        ConversionProgress?.Invoke(this, $"警告:坐标超出范围(x:{x}, y:{y}, 尺寸:{width}x{height})");
                        continue;
                    }

                    byte index = indices[i];
                    if (index < palette.Count)
                    {
                        bitmap.SetPixel(x, y, palette[index]);
                    }
                    else
                    {
                        bitmap.SetPixel(x, y, Color.Magenta);
                    }
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"创建索引位图失败:{ex.Message}");
                return null;
            }
        }
        private Bitmap? CreateArgbBitmap(byte[] fileData, int dataOffset, int width, int height)
        {
            try
            {
                List<Color> pixels = new List<Color>();
                int totalPixels = width * height;

                while (pixels.Count < totalPixels)
                {
                    if (dataOffset + 5 > fileData.Length)
                    {
                        ConversionError?.Invoke(this, "ARGB数据不完整");
                        return null;
                    }

                    byte a = fileData[dataOffset];
                    byte r = fileData[dataOffset + 1];
                    byte g = fileData[dataOffset + 2];
                    byte b = fileData[dataOffset + 3];
                    byte runLength = fileData[dataOffset + 4];
                    dataOffset += 5;
                    Color color = Color.FromArgb(a, r, g, b);
                    int remainingPixels = totalPixels - pixels.Count;
                    int actualRunLength = Math.Min(runLength, remainingPixels);

                    for (int i = 0; i < actualRunLength; i++)
                    {
                        pixels.Add(color);
                    }
                    if (pixels.Count >= totalPixels)
                        break;
                }

                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                for (int i = 0; i < pixels.Count && i < totalPixels; i++)
                {
                    int x = i % width;
                    int y = i / width;
                    bitmap.SetPixel(x, y, pixels[i]);
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"创建ARGB位图失败:{ex.Message}");
                return null;
            }
        }
        private Bitmap? CreateLumaBitmap(byte[] fileData, int dataOffset, int width, int height)
        {
            try
            {
                List<ushort> lumaValues = new List<ushort>();
                int totalPixels = width * height;

                while (lumaValues.Count < totalPixels)
                {
                    if (dataOffset + 3 > fileData.Length)
                    {
                        ConversionError?.Invoke(this, "亮度数据不完整");
                        return null;
                    }

                    ushort luma = BitConverter.ToUInt16(fileData, dataOffset);
                    byte runLength = fileData[dataOffset + 2];
                    dataOffset += 3;
                    int remainingPixels = totalPixels - lumaValues.Count;
                    int actualRunLength = Math.Min(runLength, remainingPixels);

                    for (int i = 0; i < actualRunLength; i++)
                    {
                        lumaValues.Add(luma);
                    }
                    if (lumaValues.Count >= totalPixels)
                        break;
                }

                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                for (int i = 0; i < lumaValues.Count && i < totalPixels; i++)
                {
                    int x = i % width;
                    int y = i / width;
                    ushort luma = lumaValues[i];
                    byte intensity = (byte)(luma >> 8);
                    Color color = Color.FromArgb(255, intensity, intensity, intensity);
                    bitmap.SetPixel(x, y, color);
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"创建亮度位图失败:{ex.Message}");
                return null;
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
