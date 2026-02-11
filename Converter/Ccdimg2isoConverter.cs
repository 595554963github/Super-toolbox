namespace super_toolbox
{
    public class Ccdimg2isoConverter : BaseExtractor
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

            var imgFiles = Directory.GetFiles(directoryPath, "*.img", SearchOption.AllDirectories);
            TotalFilesToConvert = imgFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var imgFilePath in imgFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(imgFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.img");

                    string fileDirectory = Path.GetDirectoryName(imgFilePath) ?? string.Empty;

                    try
                    {
                        string ccdFile = Path.Combine(fileDirectory, $"{fileName}.ccd");
                        if (!File.Exists(ccdFile))
                        {
                            ConversionError?.Invoke(this, $"未找到对应的CCD文件:{fileName}.ccd");
                            OnConversionFailed($"未找到对应的CCD文件:{fileName}.ccd");
                            continue;
                        }

                        string isoFile = Path.Combine(fileDirectory, $"{fileName}.iso");

                        if (File.Exists(isoFile))
                            File.Delete(isoFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertImgToIso(imgFilePath, isoFile, cancellationToken));

                        if (conversionSuccess && File.Exists(isoFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(isoFile)}");
                            OnFileConverted(isoFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.img转换失败");
                            OnConversionFailed($"{fileName}.img转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.img处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionError?.Invoke(this, $"转换完成,共转换0个文件");
                    OnConversionFailed($"转换完成,共转换0个文件");
                }

                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
        }

        private bool ConvertImgToIso(string imgPath, string isoPath, CancellationToken cancellationToken)
        {
            const int SECTOR_SIZE = 2352;
            const int MODE1_DATA_OFFSET = 16;
            const int MODE1_DATA_SIZE = 2048;
            const int MODE2_DATA_OFFSET = 24;
            const int MODE2_DATA_SIZE = 2048;

            using (var img = new FileStream(imgPath, FileMode.Open, FileAccess.Read))
            using (var iso = new FileStream(isoPath, FileMode.Create, FileAccess.Write))
            {
                byte[] sector = new byte[SECTOR_SIZE];
                long totalSectors = img.Length / SECTOR_SIZE;
                int sectorNum = 0;
                int successCount = 0;

                while (img.Read(sector, 0, SECTOR_SIZE) == SECTOR_SIZE)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte mode = sector[15];

                    if (mode == 1)
                    {
                        iso.Write(sector, MODE1_DATA_OFFSET, MODE1_DATA_SIZE);
                        successCount++;
                    }
                    else if (mode == 2)
                    {
                        iso.Write(sector, MODE2_DATA_OFFSET, MODE2_DATA_SIZE);
                        successCount++;
                    }
                    else if (mode == 0xE2)
                    {
                        break;
                    }

                    sectorNum++;

                    if (sectorNum % 100 == 0 || sectorNum == totalSectors)
                    {
                        int percentage = (int)((sectorNum * 100L) / totalSectors);
                        ConversionProgress?.Invoke(this, $"转换进度:{percentage}% ({sectorNum}/{totalSectors})");
                    }
                }

                iso.Flush();

                if (successCount == 0)
                {
                    return false;
                }

                long isoSize = iso.Length;
                return isoSize > 0;
            }
        }
    }
}