using GXTConvert.FileFormat;

namespace super_toolbox
{
    public class SonyGxtConverter : BaseExtractor
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

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var gxtFiles = Directory.EnumerateFiles(directoryPath, "*.gxt", SearchOption.AllDirectories)
               .Where(file => !file.Contains("Extracted", StringComparison.OrdinalIgnoreCase))
               .ToList();
            TotalFilesToConvert = gxtFiles.Count;
            int successCount = 0;
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            try
            {
                foreach (var gxtFilePath in gxtFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(gxtFilePath)}");
                    string fileName = Path.GetFileNameWithoutExtension(gxtFilePath);
                    try
                    {
                        bool conversionSuccess = await ConvertGxtToPng(gxtFilePath, extractedDir, fileName, cancellationToken);

                        if (conversionSuccess)
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(gxtFilePath)}");
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

        private async Task<bool> ConvertGxtToPng(string gxtFilePath, string outputDirectory, string fileName, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        using (FileStream fileStream = new FileStream(gxtFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            GxtBinary gxtInstance = new GxtBinary(fileStream);
                            int textureCount = 0;

                            for (int i = 0; i < gxtInstance.TextureInfos.Length; i++)
                            {
                                ThrowIfCancellationRequested(cancellationToken);
                                string outputFilename = $"{fileName}_texture_{i}.png";
                                string outputFilePath = GetUniqueFilePath(outputDirectory, outputFilename);
                                gxtInstance.Textures[i].Save(outputFilePath, System.Drawing.Imaging.ImageFormat.Png);
                                textureCount++;
                                OnFileConverted(outputFilePath);
                                ConversionProgress?.Invoke(this, $"已转换:{Path.GetFileName(outputFilePath)}");
                            }

                            if (gxtInstance.BUVChunk != null)
                            {
                                for (int i = 0; i < gxtInstance.BUVTextures.Length; i++)
                                {
                                    ThrowIfCancellationRequested(cancellationToken);

                                    string outputFilename = $"{fileName}_block_{i}.png";
                                    string outputFilePath = GetUniqueFilePath(outputDirectory, outputFilename);

                                    gxtInstance.BUVTextures[i].Save(outputFilePath, System.Drawing.Imaging.ImageFormat.Png);
                                    textureCount++;

                                    OnFileConverted(outputFilePath);
                                    ConversionProgress?.Invoke(this, $"已转换:{Path.GetFileName(outputFilePath)}");
                                }
                            }
                            return textureCount > 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换GXT文件时出错:{ex.Message}");
                        return false;
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return false;
            }
        }

        private string GetUniqueFilePath(string directory, string originalFileName)
        {
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
            string fileExtension = Path.GetExtension(originalFileName);
            string baseFilePath = Path.Combine(directory, originalFileName);
            if (!File.Exists(baseFilePath))
            {
                return baseFilePath;
            }
            int counter = 1;
            string newFilePath;
            do
            {
                string newFileName = $"{fileNameWithoutExtension}_{counter}{fileExtension}";
                newFilePath = Path.Combine(directory, newFileName);
                counter++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("转换操作已取消", cancellationToken);
            }
        }
    }
}