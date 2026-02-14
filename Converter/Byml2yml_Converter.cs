using System.Text;
using BymlLibrary;

namespace super_toolbox
{
    public class Byml2yml_Converter : BaseExtractor
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
            ConversionStarted?.Invoke(this, $"开始处理目录: {directoryPath}");
            var bymlFiles = Directory.EnumerateFiles(directoryPath, "*.byml", SearchOption.AllDirectories).ToList();
            TotalFilesToConvert = bymlFiles.Count;
            int successCount = 0;

            try
            {
                foreach (var filePath in bymlFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                    string ymlFilePath = Path.Combine(fileDirectory, $"{fileName}.yml");
                    try
                    {
                        byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

                        if (fileBytes.Length < 4)
                        {
                            ConversionError?.Invoke(this, "文件太小,不是有效的BYML文件");
                            OnConversionFailed($"{Path.GetFileName(filePath)}转换失败");
                            continue;
                        }

                        Byml byml = Byml.FromBinary(fileBytes);
                        string yaml = byml.ToYaml();

                        string? directory = Path.GetDirectoryName(ymlFilePath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        await File.WriteAllTextAsync(ymlFilePath, yaml, Encoding.UTF8, cancellationToken);

                        successCount++;
                        convertedFiles.Add(ymlFilePath);
                        ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(ymlFilePath)}");
                        OnFileConverted(ymlFilePath);
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"{Path.GetFileName(filePath)}转换失败: {ex.Message}");
                        OnConversionFailed($"{Path.GetFileName(filePath)}转换失败");
                    }
                }
                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
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

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}