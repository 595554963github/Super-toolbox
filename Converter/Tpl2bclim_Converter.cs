namespace super_toolbox
{
    public class Tpl2bclim_Converter : BaseExtractor
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

            var tplFiles = Directory.EnumerateFiles(directoryPath, "*.tpl", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = tplFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var tplFilePath in tplFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(tplFilePath)}");

                    string fileName = Path.GetFileNameWithoutExtension(tplFilePath);
                    string fileDirectory = Path.GetDirectoryName(tplFilePath) ?? string.Empty;
                    string bclimFilePath = Path.Combine(fileDirectory, $"{fileName}.bclim");

                    try
                    {
                        bool conversionSuccess = await ConvertTplToBclimAsync(tplFilePath, bclimFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(bclimFilePath))
                        {
                            successCount++;
                            convertedFiles.Add(bclimFilePath);
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(bclimFilePath)}");
                            OnFileConverted(bclimFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{Path.GetFileName(tplFilePath)}转换失败");
                            OnConversionFailed($"{Path.GetFileName(tplFilePath)}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{Path.GetFileName(tplFilePath)}处理错误:{ex.Message}");
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

        private async Task<bool> ConvertTplToBclimAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                ConversionProgress?.Invoke(this, $"开始转换{Path.GetFileName(inputPath)}");

                TM tm = new TM();
                string result = tm.TPLToBCLIM(inputPath);

                if (result == outputPath || File.Exists(result))
                {
                    if (result != outputPath && File.Exists(result))
                    {
                        if (File.Exists(outputPath))
                            File.Delete(outputPath);
                        File.Move(result, outputPath);
                    }

                    ConversionProgress?.Invoke(this, $"{Path.GetFileName(inputPath)}已成功转换为BCLIM格式");
                    return true;
                }
                else
                {
                    ConversionError?.Invoke(this, $"转换失败:{result}");
                    return false;
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
        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private class TM
        {
            public string TPLToBCLIM(string path)
            {
                string newname = Path.GetDirectoryName(path) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(path) + ".bclim";
                byte[] data = File.ReadAllBytes(path);
                byte[] tpldata = new byte[data.Length - 0x100];
                Array.Copy(data, 0x100, tpldata, 0, tpldata.Length);
                byte[] climfooter = { (byte)0x43, (byte)0x4C, (byte)0x49, (byte)0x4D, (byte)0xFF, (byte)0xFE, (byte)0x14, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x02, (byte)0x02, (byte)0x28, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x01, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x69, (byte)0x6D, (byte)0x61, (byte)0x67, (byte)0x10, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x40, (byte)0x00, (byte)0x00 };
                byte format = data[data[BitConverter.ToUInt16(data, 0x8)] + 4];
                byte[] formats = new byte[] { 0x2, 0x3, 0x2, 0x3, 0x5, 0x8, 0x9 };
                if (format < formats.Length)
                {
                    climfooter[0x20] = formats[format];
                }
                else if (format == 0x8 || format == 0x9)
                    climfooter[0x20] = 0x8;
                else if (format == 0xE)
                    climfooter[0x20] = 0xA;
                else
                {
                    throw new ArgumentException("未知TPL格式:" + format.ToString("X"));
                }
                Array.Copy(data, BitConverter.ToUInt32(data, 0x1C) + 4, climfooter, 0x1C, 4);
                Array.Copy(BitConverter.GetBytes(tpldata.Length), 0, climfooter, 0x24, 4);
                Array.Copy(BitConverter.GetBytes(tpldata.Length + 0x28), 0, climfooter, 0x0C, 4);
                File.WriteAllBytes(newname, tpldata.Concat(climfooter).ToArray());
                return newname;
            }
        }
    }
}