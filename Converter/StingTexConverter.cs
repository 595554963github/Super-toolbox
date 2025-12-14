using DALLib.File;
using DALLib.IO;
using System.IO.Compression;

namespace super_toolbox
{
    public class StingTexConverter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static readonly byte[] TARGET_HEADER = { 0x5A, 0x4C, 0x49, 0x42 };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
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

            var filePaths = Directory.GetFiles(directoryPath, "*.tex", SearchOption.AllDirectories);
            TotalFilesToConvert = filePaths.Length;
            int successCount = 0;

            try
            {
                foreach (var filePath in filePaths)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.tex");

                    try
                    {
                        bool conversionSuccess = await ConvertTexFileAsync(filePath, cancellationToken);

                        if (conversionSuccess)
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(filePath)}");
                            OnFileConverted(filePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.tex转换失败");
                            OnConversionFailed($"{fileName}.tex转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.tex处理错误:{ex.Message}");
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

        private async Task<bool> ConvertTexFileAsync(string filePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    byte[] fileData;
                    using (var fs = File.OpenRead(filePath))
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        fileData = ms.ToArray();
                    }

                    bool isTargetHeader = fileData.Length >= 12 && fileData.Take(4).SequenceEqual(TARGET_HEADER);
                    if (isTargetHeader)
                    {
                        fileData = fileData.Skip(12).ToArray();

                        if (fileData.Length >= 2 && fileData[0] == 0x78 && fileData[1] == 0xDA)
                        {
                            try
                            {
                                fileData = DecompressZlib(fileData);
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidDataException($"ZLIB解压失败:{ex.Message}");
                            }
                        }
                    }

                    using (var ms = new MemoryStream(fileData))
                    using (var reader = new ExtendedBinaryReader(ms))
                    {
                        var texFile = new TEXFile();
                        texFile.Load(reader);

                        string pngFilePath = Path.ChangeExtension(filePath, ".png");

                        texFile.SaveSheetImage(pngFilePath);

                        if (File.Exists(pngFilePath))
                        {
                            FileInfo pngInfo = new FileInfo(pngFilePath);
                            if (pngInfo.Length > 0)
                            {
                                return true;
                            }
                            else
                            {
                                File.Delete(pngFilePath); 
                                return false;
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"转换TEX文件失败:{ex.Message}\n文件:{Path.GetFileName(filePath)}", ex);
                }
            }, cancellationToken);
        }

        private byte[] DecompressZlib(byte[] compressedData)
        {
            using (var inputMs = new MemoryStream(compressedData, 2, compressedData.Length - 2))
            using (var zlibStream = new DeflateStream(inputMs, CompressionMode.Decompress))
            using (var outputMs = new MemoryStream())
            {
                zlibStream.CopyTo(outputMs);
                return outputMs.ToArray();
            }
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
