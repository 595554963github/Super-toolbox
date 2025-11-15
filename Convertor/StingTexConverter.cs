using System.IO.Compression;
using DALLib.File;
using DALLib.IO;
using DALLib.Imaging;

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
            List<string> convertedFiles = new List<string>();
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*.tex", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase));

            TotalFilesToConvert = 0;

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ConversionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    await ProcessTexFileAsync(filePath, extractedDir, convertedFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ConversionError?.Invoke(this, "转换操作已取消");
                    OnConversionFailed("转换操作已取消");
                    throw;
                }
                catch (IOException e)
                {
                    ConversionError?.Invoke(this, $"读取文件{filePath}时出错:{e.Message}");
                    OnConversionFailed($"读取文件{filePath}时出错:{e.Message}");
                }
                catch (Exception e)
                {
                    ConversionError?.Invoke(this, $"处理文件{filePath}时出错:{e.Message}");
                    OnConversionFailed($"处理文件{filePath}时出错:{e.Message}");
                }
            }

            TotalFilesToConvert = convertedFiles.Count;
            if (convertedFiles.Count > 0)
            {
                ConversionProgress?.Invoke(this, $"处理完成，共转换了{convertedFiles.Count}个PNG文件");
            }
            else
            {
                ConversionProgress?.Invoke(this, "处理完成，未找到可转换的TEX文件");
            }
            OnConversionCompleted();
        }

        private async Task ProcessTexFileAsync(string filePath, string extractedDir, List<string> convertedFiles, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
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

                string texName = Path.GetFileNameWithoutExtension(filePath);
                string texOutputDir = Path.Combine(extractedDir, texName);
                Directory.CreateDirectory(texOutputDir);

                using (var ms = new MemoryStream(fileData))
                using (var reader = new ExtendedBinaryReader(ms))
                {
                    var texFile = new TEXFile();
                    texFile.Load(reader);

                    string outputPath = Path.Combine(texOutputDir, $"{texName}.png");

                    if (texFile.SheetData != null)
                    {
                        ImageTools.SaveImage(outputPath,
                            texFile.SheetWidth,
                            texFile.SheetHeight,
                            texFile.SheetData);

                        if (!convertedFiles.Contains(outputPath))
                        {
                            convertedFiles.Add(outputPath);
                            OnFileConverted(outputPath);
                            ConversionProgress?.Invoke(this, $"已转换:{Path.GetFileName(outputPath)}");
                        }
                    }
                    else
                    {
                        throw new InvalidDataException("TEX文件不包含有效的图像数据");
                    }
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