using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LZ4;

namespace super_toolbox
{
    public class Sct2Png_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static readonly byte[] SCT_SIGNATURE = { 0x53, 0x43, 0x54, 0x01 };
        private static readonly int SIGNATURE_LENGTH = SCT_SIGNATURE.Length;

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
            var sctFiles = Directory.EnumerateFiles(directoryPath, "*.sct", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = sctFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var sctFilePath in sctFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(sctFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(sctFilePath);
                    string fileDirectory = Path.GetDirectoryName(sctFilePath) ?? string.Empty;
                    fileName = fileName.Replace(".png", "", StringComparison.OrdinalIgnoreCase);
                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    try
                    {
                        bool conversionSuccess = await ConvertSctToPngAsync(sctFilePath, pngFilePath, cancellationToken);
                        if (conversionSuccess && File.Exists(pngFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(pngFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngFilePath)}");
                            OnFileConverted(pngFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.sct转换失败");
                            OnConversionFailed($"{fileName}.sct转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.sct处理错误:{ex.Message}");
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

        private async Task<bool> ConvertSctToPngAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                await using (FileStream fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
                {
                    byte[] signature = new byte[SIGNATURE_LENGTH];
                    int bytesRead = await fs.ReadAsync(signature, 0, SIGNATURE_LENGTH, cancellationToken);
                    if (bytesRead != SIGNATURE_LENGTH || !signature.SequenceEqual(SCT_SIGNATURE))
                    {
                        ConversionError?.Invoke(this, "不是有效的SCT文件");
                        return false;
                    }

                    int texType = fs.ReadByte();
                    if (texType == -1)
                    {
                        ConversionError?.Invoke(this, "无法读取纹理类型");
                        return false;
                    }

                    byte[] widthBytes = await ReadBytesAsync(fs, 2, cancellationToken);
                    int width = BitConverter.ToInt16(widthBytes, 0);

                    byte[] heightBytes = await ReadBytesAsync(fs, 2, cancellationToken);
                    int height = BitConverter.ToInt16(heightBytes, 0);

                    byte[] decLenBytes = await ReadBytesAsync(fs, 4, cancellationToken);
                    int decLen = BitConverter.ToInt32(decLenBytes, 0);

                    byte[] comLenBytes = await ReadBytesAsync(fs, 4, cancellationToken);
                    int comLen = BitConverter.ToInt32(comLenBytes, 0);

                    byte[] compressedData = await ReadBytesAsync(fs, comLen, cancellationToken);
                    byte[] data = LZ4Codec.Decode(compressedData, 0, comLen, decLen);

                    Bitmap bitmap;
                    if (texType == 2)
                    {
                        bitmap = CreateBitmapFromRGBA(data, width, height);
                    }
                    else if (texType == 4)
                    {
                        bitmap = CreateBitmapFromRGBA5551(data, width, height);
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"不支持的纹理类型:{texType}");
                        return false;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    bitmap.Save(outputPath, ImageFormat.Png);
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

        private async Task<byte[]> ReadBytesAsync(Stream stream, int count, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[count];
            int bytesRead = 0;
            while (bytesRead < count)
            {
                int read = await stream.ReadAsync(buffer, bytesRead, count - bytesRead, cancellationToken);
                if (read == 0)
                    throw new EndOfStreamException();
                bytesRead += read;
            }
            return buffer;
        }

        private Bitmap CreateBitmapFromRGBA(byte[] data, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(data, 0, bmpData.Scan0, data.Length);
            bitmap.UnlockBits(bmpData);
            return bitmap;
        }

        private Bitmap CreateBitmapFromRGBA5551(byte[] data, int width, int height)
        {
            int start = width * height * 2;
            byte[] rgb555Data = new byte[start];
            byte[] alphaData = new byte[width * height];

            Array.Copy(data, 0, rgb555Data, 0, start);
            Array.Copy(data, start, alphaData, 0, width * height);

            byte[] rgbaData = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                ushort rgb555 = BitConverter.ToUInt16(rgb555Data, i * 2);
                byte r = (byte)(((((rgb555 >> 10) & 0x1F) * 527) + 23) >> 6);
                byte g = (byte)(((((rgb555 >> 5) & 0x1F) * 527) + 23) >> 6);
                byte b = (byte)(((rgb555 & 0x1F) * 527 + 23) >> 6);
                byte a = alphaData[i];

                rgbaData[i * 4] = r;
                rgbaData[i * 4 + 1] = g;
                rgbaData[i * 4 + 2] = b;
                rgbaData[i * 4 + 3] = a;
            }

            return CreateBitmapFromRGBA(rgbaData, width, height);
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