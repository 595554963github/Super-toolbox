using GXTConvert;
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
            TotalFilesToConvert = gxtFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var gxtFilePath in gxtFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(gxtFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.gxt");

                    string fileDirectory = Path.GetDirectoryName(gxtFilePath) ?? string.Empty;

                    try
                    {
                        bool conversionSuccess = await ConvertGxtToPng(gxtFilePath, fileDirectory, cancellationToken);

                        if (conversionSuccess)
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{fileName}.gxt");
                            OnFileConverted(gxtFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.gxt转换失败");
                            OnConversionFailed($"{fileName}.gxt转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.gxt处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成，成功转换{successCount}/{TotalFilesToConvert}个GXT文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成，但未成功转换任何GXT文件");
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

        private async Task<bool> ConvertGxtToPng(string gxtFilePath, string outputDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(gxtFilePath);
                string baseOutputName = Path.Combine(outputDirectory, fileName);

                using (FileStream fs = new FileStream(gxtFilePath, FileMode.Open, FileAccess.Read))
                using (GxtBinary gxt = new GxtBinary(fs))
                {
                    ConversionProgress?.Invoke(this, $"[GXT]正在保存{fileName}.gxt的纹理...");
                    var pngEncoder = new PngEncoder();

                    for (int i = 0; i < gxt.Textures.Length; i++)
                    {
                        string outputPath = gxt.Textures.Length == 1
                            ? $"{baseOutputName}.png"
                            : $"{baseOutputName}_{i + 1}.png";

                        await using (var outputStream = File.Create(outputPath))
                        {
                            await gxt.Textures[i].SaveAsync(outputStream, pngEncoder, cancellationToken);
                        }

                        ConversionProgress?.Invoke(this, $"[GXT]已保存:{Path.GetFileName(outputPath)}");
                    }

                    if (gxt.BUVTextures != null && gxt.BUVTextures.Length > 0)
                    {
                        ConversionProgress?.Invoke(this, $"[GXT]正在保存{fileName}.gxt的BUV纹理...");
                        for (int i = 0; i < gxt.BUVTextures.Length; i++)
                        {
                            string outputPath = gxt.BUVTextures.Length == 1
                                ? $"{baseOutputName}_buv.png"
                                : $"{baseOutputName}_buv_{i + 1}.png";
                            await using (var outputStream = File.Create(outputPath))
                            {
                                await gxt.BUVTextures[i].SaveAsync(outputStream, pngEncoder, cancellationToken);
                            }

                            ConversionProgress?.Invoke(this, $"[GXT]已保存BUV纹理:{Path.GetFileName(outputPath)}");
                        }
                    }

                    ConversionProgress?.Invoke(this, $"[GXT]{fileName}.gxt转换完成");
                    return true;
                }
            }
            catch (FormatNotImplementedException ex)
            {
                ConversionError?.Invoke(this, $"[GXT]不支持的纹理格式:{ex.Format}");
                return false;
            }
            catch (TypeNotImplementedException ex)
            {
                ConversionError?.Invoke(this, $"[GXT]不支持的纹理类型:{ex.Type}");
                return false;
            }
            catch (VersionNotImplementedException ex)
            {
                ConversionError?.Invoke(this, $"[GXT]不支持的GXT版本:0x{ex.Version:X8}");
                return false;
            }
            catch (UnknownMagicException)
            {
                ConversionError?.Invoke(this, $"[GXT]未知的文件格式或损坏的文件");
                return false;
            }
            catch (PaletteNotImplementedException ex)
            {
                ConversionError?.Invoke(this, $"[GXT]不支持的调色板格式:{ex.Format}");
                return false;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"[GXT]转换过程异常:{ex.Message}");
                return false;
            }
        }
    }
}
