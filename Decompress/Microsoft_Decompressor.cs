using System.Runtime.InteropServices;
using System.Reflection;

namespace super_toolbox
{
    public class Microsoft_Decompressor : BaseExtractor
    {
        private static readonly string _tempDllPath = Path.Combine(Path.GetTempPath(), "supertoolbox_temp", "XboxCompress.dll");
        private static bool _isInitialized;
        private static string _initError = string.Empty;

        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        static Microsoft_Decompressor()
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

        private delegate int DecompressFileDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceFile,
            [MarshalAs(UnmanagedType.LPWStr)] string destFile,
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

        private string? InvokeDecompress(string inputPath, string outputPath)
        {
            if (!_isInitialized) return _initError;

            IntPtr hDll = LoadLibrary(_tempDllPath);
            if (hDll == IntPtr.Zero) return "加载DLL失败";

            try
            {
                IntPtr pDecompressFile = GetProcAddress(hDll, "DecompressFile");
                if (pDecompressFile == IntPtr.Zero) return "未找到DecompressFile函数";

                IntPtr pGetLastError = GetProcAddress(hDll, "GetLastErrorMessage");
                if (pGetLastError == IntPtr.Zero) return "未找到GetLastErrorMessage函数";

                var decompressFile = Marshal.GetDelegateForFunctionPointer<DecompressFileDelegate>(pDecompressFile);
                var getLastError = Marshal.GetDelegateForFunctionPointer<GetLastErrorMessageDelegate>(pGetLastError);

                int result = decompressFile(inputPath, outputPath, 0, 1);
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
                DecompressionError?.Invoke(this, $"初始化失败:{_initError}");
                OnDecompressionFailed($"初始化失败:{_initError}");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);

                    if (files.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "没有可解压的文件");
                        OnDecompressionFailed("没有可解压的文件");
                        return;
                    }

                    string outputDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(outputDir);

                    TotalFilesToDecompress = files.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压,共{TotalFilesToDecompress}个文件");

                    int successCount = 0;
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(file);
                        string outputFileName = fileName.EndsWith("_") ? fileName[0..^1] : fileName;
                        string outputPath = Path.Combine(outputDir, outputFileName);

                        DecompressionProgress?.Invoke(this, $"正在解压:{fileName}");

                        string? error = InvokeDecompress(file, outputPath);
                        if (error == null)
                        {
                            successCount++;
                            OnFileDecompressed(outputPath);
                        }
                        else
                        {
                            DecompressionError?.Invoke(this, $"失败:{fileName}-{error}");
                        }
                    }

                    if (successCount > 0)
                    {
                        OnDecompressionCompleted();
                        DecompressionProgress?.Invoke(this, $"解压完成,共解压{successCount}/{TotalFilesToDecompress}个文件");
                    }
                    else
                    {
                        DecompressionError?.Invoke(this, "所有文件均解压失败");
                        OnDecompressionFailed("所有文件均解压失败");
                    }

                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                DecompressionError?.Invoke(this, "解压操作已取消");
                OnDecompressionFailed("解压操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"解压失败:{ex.Message}");
                OnDecompressionFailed($"解压失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}