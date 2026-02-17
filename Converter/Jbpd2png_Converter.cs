using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Jbpd2png_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private static readonly byte[] JBPD_MAGIC_HEADER = new byte[] { 0x4A, 0x42, 0x50, 0x44, 0x2C, 0x00, 0x00, 0x00 };

        private static string _tempDllPath;

        static Jbpd2png_Converter()
        {
            _tempDllPath = LoadEmbeddedDll("embedded.jbpd2png.dll", "jbpd2png.dll");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ConvertJBPDToPNGDelegate(string jbpdFile, string pngFile);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ConvertJBPDToPNGWithSharpnessDelegate(string jbpdFile, string pngFile, int sharpness);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetVersionDelegate();

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var jbpdFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => IsJBPDFile(f))
                .OrderBy(f => ExtractNumberFromFileName(f))
                .ThenBy(f => f)
                .ToArray();

            TotalFilesToConvert = jbpdFiles.Length;
            int successCount = 0;

            try
            {
                IntPtr dllHandle = IntPtr.Zero;

                for (int i = 0; i < jbpdFiles.Length; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string jbpdFilePath = jbpdFiles[i];
                    string fileName = Path.GetFileNameWithoutExtension(jbpdFilePath);
                    string fileDirectory = Path.GetDirectoryName(jbpdFilePath) ?? string.Empty;
                    string pngFilePath = Path.Combine(fileDirectory, $"{fileName}.png");

                    ConversionProgress?.Invoke(this, $"正在处理:[{i + 1}/{TotalFilesToConvert}] {fileName}");

                    try
                    {
                        if (dllHandle == IntPtr.Zero)
                        {
                            dllHandle = LoadLibrary(_tempDllPath);
                            if (dllHandle == IntPtr.Zero)
                            {
                                ConversionError?.Invoke(this, "无法加载JBPD转换DLL");
                                OnConversionFailed("无法加载JBPD转换DLL");
                                return;
                            }
                        }

                        bool conversionSuccess = await ConvertJBPDToPNG(jbpdFilePath, pngFilePath, dllHandle, cancellationToken);

                        if (conversionSuccess)
                        {
                            if (File.Exists(pngFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"转换成功:[{i + 1}/{TotalFilesToConvert}] {Path.GetFileName(pngFilePath)}");
                                OnFileConverted(pngFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}转换成功但未找到输出文件");
                                OnConversionFailed($"{fileName}转换成功但未找到输出文件");
                            }
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

                if (dllHandle != IntPtr.Zero)
                {
                    FreeLibrary(dllHandle);
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件（按数字顺序）");
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

        private bool IsJBPDFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < JBPD_MAGIC_HEADER.Length)
                        return false;

                    byte[] headerBytes = new byte[JBPD_MAGIC_HEADER.Length];
                    int bytesRead = fs.Read(headerBytes, 0, JBPD_MAGIC_HEADER.Length);

                    if (bytesRead < JBPD_MAGIC_HEADER.Length)
                        return false;

                    return headerBytes.SequenceEqual(JBPD_MAGIC_HEADER);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private int ExtractNumberFromFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            Match match = Regex.Match(fileName, @"_(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
            {
                return number;
            }
            return int.MaxValue;
        }

        private async Task<bool> ConvertJBPDToPNG(string jbpdFilePath, string pngFilePath, IntPtr dllHandle, CancellationToken cancellationToken)
        {
            try
            {
                IntPtr convertFuncPtr = GetProcAddress(dllHandle, "ConvertJBPDToPNG");
                if (convertFuncPtr == IntPtr.Zero)
                {
                    ConversionError?.Invoke(this, "无法找到转换函数");
                    return false;
                }

                ConvertJBPDToPNGDelegate convertFunction = (ConvertJBPDToPNGDelegate)Marshal.GetDelegateForFunctionPointer(
                    convertFuncPtr, typeof(ConvertJBPDToPNGDelegate));

                int result = await Task.Run(() => convertFunction(jbpdFilePath, pngFilePath), cancellationToken);

                return result == 0;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换过程异常:{ex.Message}");
                return false;
            }
        }

        private new static string LoadEmbeddedDll(string resourceName, string outputFileName)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "super_toolbox");
            Directory.CreateDirectory(tempPath);

            string dllPath = Path.Combine(tempPath, outputFileName);

            if (!File.Exists(dllPath))
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException($"嵌入的资源'{resourceName}'未找到");
                    }

                    using (FileStream fileStream = new FileStream(dllPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }
                }
            }

            return dllPath;
        }
    }
}