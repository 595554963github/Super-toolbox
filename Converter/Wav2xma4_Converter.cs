using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2xma4_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        private const int ADPCM_FLAG_NOISE_SHAPING = 0x1;
        private const int ADPCM_SUCCESS = 0;

        [DllImport("IMA_codec.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADPCM_EncodeFile(string inputFile, string outputFile, int flags, int lookahead, int blocksize_pow2, int encode_width_bits, double static_shaping_weight);

        static Wav2xma4_Converter()
        {
            LoadEmbeddedDll("embedded.IMA_codec.dll", "IMA_codec.dll");
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

            var wavFiles = Directory.GetFiles(directoryPath, "*.wav", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = wavFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}");

                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                    string xmaFilePath = Path.Combine(fileDirectory, $"{fileName}.xma");

                    try
                    {
                        bool conversionSuccess = await ConvertWAVToXMA(wavFilePath, xmaFilePath, cancellationToken);

                        if (conversionSuccess && File.Exists(xmaFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(xmaFilePath)}");
                            OnFileConverted(xmaFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}转换失败");
                            OnConversionFailed($"{fileName}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}处理错误:{ex.Message}");
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

        private async Task<bool> ConvertWAVToXMA(string wavFilePath, string xmaFilePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int result = ADPCM_EncodeFile(wavFilePath, xmaFilePath, ADPCM_FLAG_NOISE_SHAPING, 3, 0, 4, 0.0);
                    return result == ADPCM_SUCCESS;
                }
                catch (Exception ex)
                {
                    ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }
    }
}
