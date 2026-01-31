using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Ahx2wav_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;
        private static string _tempDllPath;

        [DllImport("ahx2wav.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ConvertAhxToWav(string ahxPath, string wavPath, int verboseMode);

        static Ahx2wav_Converter()
        {
            _tempDllPath = LoadEmbeddedExe("embedded.ahx2wav.dll", "ahx2wav.dll");
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

            var ahxFiles = Directory.GetFiles(directoryPath, "*.ahx", SearchOption.AllDirectories);
            TotalFilesToConvert = ahxFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var ahxFilePath in ahxFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(ahxFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.ahx");

                    string fileDirectory = Path.GetDirectoryName(ahxFilePath) ?? string.Empty;

                    try
                    {
                        string wavFile = Path.Combine(fileDirectory, $"{fileName}.wav");

                        if (File.Exists(wavFile))
                            File.Delete(wavFile);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertAhxToWavDll(ahxFilePath, wavFile, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFile)}");
                            OnFileConverted(wavFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.ahx转换失败");
                            OnConversionFailed($"{fileName}.ahx转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.ahx处理错误:{ex.Message}");
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

        private bool ConvertAhxToWavDll(string ahxFilePath, string wavFile, CancellationToken cancellationToken)
        {
            try
            {
                int result = ConvertAhxToWav(ahxFilePath, wavFile, 0);

                if (result == 0)
                {
                    return true;
                }
                else
                {
                    string errorMsg = result switch
                    {
                        -1 => "无法打开AHX文件",
                        -2 => "内存分配失败",
                        -3 => "读取文件失败",
                        -4 => "WAV缓冲区分配失败",
                        -5 => "无法创建WAV文件",
                        -6 => "写入WAV文件失败",
                        _ => $"未知错误 (代码: {result})"
                    };

                    ConversionError?.Invoke(this, $"DLL转换错误:{errorMsg}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"DLL调用异常:{ex.Message}");
                return false;
            }
        }
    }
}
