using PuyoTools.Core.Textures.Gim;

namespace super_toolbox
{
    public class GIM2PNG_Converter : BaseExtractor
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
            var gimFiles = Directory.EnumerateFiles(directoryPath, "*.gim", SearchOption.AllDirectories).ToArray();
            TotalFilesToConvert = gimFiles.Length;

            int successCount = 0;

            try
            {
                foreach (var gimFilePath in gimFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(gimFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(gimFilePath);
                    string fileDirectory = Path.GetDirectoryName(gimFilePath) ?? string.Empty;

                    fileName = fileName.Replace(".png", "", StringComparison.OrdinalIgnoreCase);

                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    try
                    {
                        bool conversionSuccess = await ConvertGimToPng(gimFilePath, pngFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(pngFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngFilePath)}");
                            OnFileConverted(pngFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.gim转换失败");
                            OnConversionFailed($"{fileName}.gim转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.gim处理错误:{ex.Message}");
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

        private async Task<bool> ConvertGimToPng(string gimFilePath, string pngFilePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string? outputDir = Path.GetDirectoryName(pngFilePath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                        Directory.CreateDirectory(outputDir);

                    using (var stream = File.OpenRead(gimFilePath))
                    {
                        if (!GimTextureDecoder.Is(stream))
                            return false;

                        stream.Position = 0;
                        var decoder = new GimTextureDecoder(stream);
                        decoder.Save(pngFilePath);
                        return true;
                    }
                }
                catch (Exception)
                {
                    if (File.Exists(pngFilePath))
                    {
                        try { File.Delete(pngFilePath); } catch { }
                    }
                    return false;
                }
            }, cancellationToken);
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

                string pngFilePath = outputPath ?? Path.ChangeExtension(inputPath, ".png");

                using (var stream = File.OpenRead(inputPath))
                {
                    if (!GimTextureDecoder.Is(stream))
                    {
                        ConversionError?.Invoke(this, $"不是有效的GIM文件:{Path.GetFileName(inputPath)}");
                        return false;
                    }

                    stream.Position = 0;
                    var decoder = new GimTextureDecoder(stream);
                    decoder.Save(pngFilePath);

                    ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(pngFilePath)}");
                    OnFileConverted(pngFilePath);
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
            TotalFilesToConvert = inputPaths.Length;
            int successCount = 0;

            ConversionStarted?.Invoke(this, $"开始批量转换{inputPaths.Length}个文件");

            foreach (var inputPath in inputPaths)
            {
                try
                {
                    string outputPath;
                    if (!string.IsNullOrEmpty(outputDirectory))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(inputPath);
                        fileName = fileName.Replace(".png", "", StringComparison.OrdinalIgnoreCase);
                        outputPath = Path.Combine(outputDirectory, $"{fileName}.png");
                    }
                    else
                    {
                        outputPath = Path.ChangeExtension(inputPath, ".png");
                    }

                    if (ConvertSingleFile(inputPath, outputPath))
                    {
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    ConversionError?.Invoke(this, $"文件转换异常:{Path.GetFileName(inputPath)} - {ex.Message}");
                }
            }

            OnConversionCompleted();
        }
    }
}