using System.Runtime.InteropServices;
using System.Reflection;

namespace super_toolbox
{
    public class Microsoft_Compressor : BaseExtractor
    {
        private static readonly string _tempDllPath = Path.Combine(Path.GetTempPath(), "supertoolbox_temp", "XboxCompress.dll");
        private static bool _isInitialized;
        private static string _initError = string.Empty;

        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        static Microsoft_Compressor()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_tempDllPath)!);

                var assembly = Assembly.GetExecutingAssembly();
                var foundResource = assembly.GetManifestResourceNames().FirstOrDefault(name =>
                    name.ToLower().Contains("xboxcompress.dll") ||
                    name.ToLower().Contains("xboxcompress") ||
                    name.EndsWith("xboxcompress.dll"));

                if (foundResource == null)
                {
                    _initError = "未找到XboxCompress.dll资源,可用资源:" + string.Join(", ", assembly.GetManifestResourceNames());
                    return;
                }

                ExtractEmbeddedResource(foundResource, _tempDllPath);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _initError = ex.Message;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private delegate int CompressFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceFile,
            [MarshalAs(UnmanagedType.LPWStr)] string destFile,
            int useMSZIP,
            int toLower,
            int overwrite
        );

        private delegate IntPtr GetLastErrorMessageDelegate();

        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            if (!File.Exists(outputPath))
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) ??
                    throw new FileNotFoundException($"嵌入的XboxCompress资源未找到:{resourceName}");

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                File.WriteAllBytes(outputPath, buffer);
            }
        }

        private string? InvokeCompress(string inputPath, string outputPath)
        {
            if (!_isInitialized) return _initError;

            IntPtr hDll = LoadLibrary(_tempDllPath);
            if (hDll == IntPtr.Zero) return "加载DLL失败";

            try
            {
                IntPtr pCompressFile = GetProcAddress(hDll, "CompressFile");
                if (pCompressFile == IntPtr.Zero) return "未找到CompressFile函数";

                IntPtr pGetLastError = GetProcAddress(hDll, "GetLastErrorMessage");
                if (pGetLastError == IntPtr.Zero) return "未找到GetLastErrorMessage函数";

                var compressFile = Marshal.GetDelegateForFunctionPointer<CompressFileDelegate>(pCompressFile);
                var getLastError = Marshal.GetDelegateForFunctionPointer<GetLastErrorMessageDelegate>(pGetLastError);

                int result = compressFile(inputPath, outputPath, 0, 0, 1);
                if (result != 0)
                {
                    IntPtr errorPtr = getLastError();
                    return Marshal.PtrToStringUni(errorPtr) ?? "未知错误";
                }

                return null;
            }
            finally
            {
                if (hDll != IntPtr.Zero) FreeLibrary(hDll);
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                CompressionError?.Invoke(this, $"初始化失败:{_initError}");
                OnCompressionFailed($"初始化失败:{_initError}");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);

                    if (files.Length == 0)
                    {
                        CompressionError?.Invoke(this, "没有可压缩的文件");
                        OnCompressionFailed("没有可压缩的文件");
                        return;
                    }

                    string outputDir = Path.Combine(directoryPath, "Compressed");
                    Directory.CreateDirectory(outputDir);

                    TotalFilesToCompress = files.Length;
                    CompressionStarted?.Invoke(this, $"开始压缩,共{TotalFilesToCompress}个文件");

                    int successCount = 0;
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(file);
                        string outputPath = Path.Combine(outputDir, fileName + "_");

                        CompressionProgress?.Invoke(this, $"正在压缩:{fileName}");

                        string? error = InvokeCompress(file, outputPath);
                        if (error == null)
                        {
                            successCount++;
                            OnFileCompressed(outputPath);
                        }
                        else
                        {
                            CompressionError?.Invoke(this, $"失败:{fileName}-{error}");
                        }
                    }

                    if (successCount > 0)
                    {
                        OnCompressionCompleted();
                        CompressionProgress?.Invoke(this, $"压缩完成,共压缩{successCount}/{TotalFilesToCompress}个文件");
                    }
                    else
                    {
                        CompressionError?.Invoke(this, "所有文件均压缩失败");
                        OnCompressionFailed("所有文件均压缩失败");
                    }

                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CompressionError?.Invoke(this, "压缩操作已取消");
                OnCompressionFailed("压缩操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"压缩失败:{ex.Message}");
                OnCompressionFailed($"压缩失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}