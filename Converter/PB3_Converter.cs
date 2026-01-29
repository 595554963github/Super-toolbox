using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class PB3_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        [DllImport("pb3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int DecodePB3File(string inputFile, string outputFile);

        static PB3_Converter()
        {
            LoadPb3Dll();
        }

        private static void LoadPb3Dll()
        {
            string dllPath = Path.Combine(TempDllDirectory, "pb3.dll");

            if (!File.Exists(dllPath))
            {
                ExtractEmbeddedResource("embedded.pb3.dll", dllPath);
            }

            NativeLibrary.Load(dllPath);
        }

        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null) throw new FileNotFoundException($"嵌入的资源'{resourceName}'未找到");

            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            File.WriteAllBytes(outputPath, buffer);
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

            var pb3Files = Directory.GetFiles(directoryPath, "*.pb3", SearchOption.AllDirectories).ToArray();
            TotalFilesToConvert = pb3Files.Length;
            int successCount = 0;

            try
            {
                foreach (var pb3FilePath in pb3Files)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    ConversionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(pb3FilePath)}");

                    try
                    {
                        string outputFile = await ConvertPb3ToBmp(pb3FilePath, cancellationToken);

                        if (!string.IsNullOrEmpty(outputFile) && File.Exists(outputFile))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(outputFile)}");
                            OnFileConverted(outputFile);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{Path.GetFileName(pb3FilePath)}转换失败");
                            OnConversionFailed($"{Path.GetFileName(pb3FilePath)}转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{Path.GetFileName(pb3FilePath)}处理错误:{ex.Message}");
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

        private async Task<string> ConvertPb3ToBmp(string pb3FilePath, CancellationToken cancellationToken)
        {
            await Task.Yield();

            try
            {
                string outputFileName = Path.ChangeExtension(pb3FilePath, ".bmp");

                int result = DecodePB3File(pb3FilePath, outputFileName);

                if (result == 0)
                {
                    return outputFileName;
                }

                string errorMessage = result switch
                {
                    -1 => "无法打开输入文件",
                    -2 => "读取文件失败",
                    -3 => "解码失败",
                    -4 => "未知异常",
                    _ => $"未知错误代码:{result}"
                };

                ConversionError?.Invoke(this, errorMessage);
                return string.Empty;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"DLL调用异常:{ex.Message}");
                return string.Empty;
            }
        }
    }
}