using GXTConvert.Exceptions;
using GXTConvert.FileFormat;
using SixLabors.ImageSharp.Formats.Png;

namespace super_toolbox
{
    public class SonyGxtConverter : BaseExtractor
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

            var gxtFiles = Directory.GetFiles(directoryPath, "*.gxt", SearchOption.AllDirectories);
            int totalPngFilesConverted = 0;
            int processedGxtFiles = 0;

            try
            {
                foreach (var gxtFilePath in gxtFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        ConversionError?.Invoke(this, "操作已取消");
                        OnConversionFailed("操作已取消");
                        return;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(gxtFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.gxt");

                    try
                    {
                        int pngCount = await ProcessGxtFileAsync(gxtFilePath, cancellationToken);

                        if (pngCount > 0)
                        {
                            totalPngFilesConverted += pngCount;
                            processedGxtFiles++;
                            ConversionProgress?.Invoke(this, $"{fileName}.gxt转换成功,生成{pngCount}个png图片");
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.gxt未生成任何png图片");
                            OnConversionFailed($"{fileName}.gxt 未生成任何png图片");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"{fileName}.gxt处理错误:{ex.Message}");
                        OnConversionFailed($"{fileName}.gxt 处理错误:{ex.Message}");
                    }
                }

                if (totalPngFilesConverted > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,处理了{processedGxtFiles}/{gxtFiles.Length}个GXT文件,共生成{totalPngFilesConverted}个png图片");
                    OnConversionCompleted();
                }
                else
                {
                    ConversionError?.Invoke(this, "未成功转换任何文件");
                    OnConversionFailed("未成功转换任何文件");
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }

        private async Task<int> ProcessGxtFileAsync(string gxtFilePath, CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileNameWithoutExtension(gxtFilePath);
            string outputDirectory = Path.GetDirectoryName(gxtFilePath)!;
            string baseOutputName = Path.Combine(outputDirectory, fileName);
            int pngCount = 0;

            try
            {
                using var fs = new FileStream(gxtFilePath, FileMode.Open, FileAccess.Read);
                var gxt = new GxtBinary(fs);

                var palettes = new Dictionary<int, Color[]>();
                for (int i = 0; i < gxt.TextureBundles.Length; i++)
                {
                    var bundle = gxt.TextureBundles[i];

                    if (bundle.TextureFormat.ToString().StartsWith("P4_") ||
                        bundle.TextureFormat.ToString().StartsWith("P8_"))
                    {
                        var palette = gxt.FetchPalette(bundle.TextureFormat, bundle.PaletteIndex);
                        palettes[i] = palette;
                    }
                }

                for (int i = 0; i < gxt.TextureBundles.Length; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return pngCount;

                    var bundle = gxt.TextureBundles[i];
                    string outputPath = GetOutputPath(baseOutputName, i, gxt.TextureBundles.Length);

                    Bitmap bitmap;
                    if (palettes.ContainsKey(i))
                    {
                        bitmap = bundle.CreateTexture(palettes[i]);
                    }
                    else
                    {
                        bitmap = bundle.CreateTexture();
                    }

                    await ConvertBitmapToImageSharpAndSaveAsync(bitmap, outputPath, cancellationToken);

                    bitmap.Dispose();

                    pngCount++;
                    ConversionProgress?.Invoke(this, $"已保存:{Path.GetFileName(outputPath)}");

                    OnFileConverted(outputPath);
                }

                return pngCount;
            }
            catch (FormatNotImplementedException ex)
            {
                ConversionError?.Invoke(this, $"不支持格式:{ex.Format}");
                return 0;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                return 0;
            }
        }

        private async Task ConvertBitmapToImageSharpAndSaveAsync(Bitmap bitmap, string outputPath, CancellationToken cancellationToken)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var image = await SixLabors.ImageSharp.Image.LoadAsync(ms, cancellationToken);
                await using var outputStream = File.Create(outputPath);
                await image.SaveAsync(outputStream, new PngEncoder(), cancellationToken);
            }
        }

        private static string GetOutputPath(string baseName, int index, int totalBundles)
        {
            return totalBundles == 1
                ? $"{baseName}.png"
                : $"{baseName}_{index + 1}.png";
        }
    }
}
