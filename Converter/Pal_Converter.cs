using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;

namespace super_toolbox
{
    public class Pal_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private const int PALETTE_SIZE = 513;
        private const int EXTRACTED_SIZE = 1024;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var palFiles = Directory.GetFiles(directoryPath, "*.pal", SearchOption.AllDirectories).ToArray();
            TotalFilesToConvert = palFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var palFilePath in palFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(palFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}");

                    try
                    {
                        string outputFile = await ConvertPalToPng(palFilePath, cancellationToken);

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

        private async Task<string> ConvertPalToPng(string palFilePath, CancellationToken cancellationToken)
        {
            await Task.Yield();

            try
            {
                using (FileStream inputStream = new FileStream(palFilePath, FileMode.Open, FileAccess.Read))
                {
                    if (inputStream.Length != PALETTE_SIZE)
                    {
                        throw new InvalidDataException($"调色板文件必须是{PALETTE_SIZE}字节");
                    }

                    string outputFileName = Path.ChangeExtension(palFilePath, ".png");

                    byte[] buffer = ArrayPool<byte>.Shared.Rent(PALETTE_SIZE);
                    try
                    {
                        inputStream.Seek(0, SeekOrigin.Begin);
                        await inputStream.ReadExactlyAsync(buffer.AsMemory(0, PALETTE_SIZE), cancellationToken).ConfigureAwait(false);

                        if (buffer[0] != 16)
                        {
                            throw new InvalidDataException("调色板必须使用16bpp颜色");
                        }

                        byte[] rgbaBuffer = await ConvertBgra5551ToRgba32(buffer, cancellationToken);

                        await CreatePaletteImage(rgbaBuffer, outputFileName, cancellationToken);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    return outputFileName;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return string.Empty;
            }
        }

        private async Task<byte[]> ConvertBgra5551ToRgba32(byte[] palBuffer, CancellationToken cancellationToken)
        {
            await Task.Yield();

            byte[] rgbaBuffer = new byte[EXTRACTED_SIZE];

            for (int i = 0; i < 256; i++)
            {
                if (i * 2 + 1 >= PALETTE_SIZE - 1) break;

                int bufferIndex = 1 + i * 2;
                ushort color = (ushort)((palBuffer[bufferIndex + 1] << 8) | palBuffer[bufferIndex]);

                int b = (color & 0x1F);
                int g = (color >> 5) & 0x1F;
                int r = (color >> 10) & 0x1F;
                int a = (color & 0x8000) != 0 ? 255 : 0;

                b = (b * 255 + 15) / 31;
                g = (g * 255 + 15) / 31;
                r = (r * 255 + 15) / 31;

                rgbaBuffer[i * 4] = (byte)r;
                rgbaBuffer[i * 4 + 1] = (byte)g;
                rgbaBuffer[i * 4 + 2] = (byte)b;
                rgbaBuffer[i * 4 + 3] = (byte)a;
            }

            return rgbaBuffer;
        }

        private async Task CreatePaletteImage(byte[] rgbaBuffer, string pngFileName, CancellationToken cancellationToken)
        {
            await Task.Yield();

            int colorsPerRow = 16;
            int colorSize = 32;
            int spacing = 2;
            int imageWidth = colorsPerRow * (colorSize + spacing) - spacing;
            int imageHeight = 16 * (colorSize + spacing) - spacing;

            using (Bitmap paletteImage = new Bitmap(imageWidth, imageHeight))
            using (Graphics g = Graphics.FromImage(paletteImage))
            {
                g.Clear(Color.Gray);

                for (int i = 0; i < 256; i++)
                {
                    int row = i / colorsPerRow;
                    int col = i % colorsPerRow;

                    int r = rgbaBuffer[i * 4];
                    int gValue = rgbaBuffer[i * 4 + 1];
                    int b = rgbaBuffer[i * 4 + 2];
                    int a = rgbaBuffer[i * 4 + 3];

                    Color color = Color.FromArgb(a, r, gValue, b);

                    using (SolidBrush brush = new SolidBrush(color))
                    {
                        int x = col * (colorSize + spacing);
                        int y = row * (colorSize + spacing);

                        g.FillRectangle(brush, x, y, colorSize, colorSize);
                        g.DrawRectangle(Pens.Black, x, y, colorSize - 1, colorSize - 1);
                    }
                }

                paletteImage.Save(pngFileName, ImageFormat.Png);
            }
        }
    }
}