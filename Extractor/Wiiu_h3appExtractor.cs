using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class Wiiu_h3appExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempDllPath;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProgressCallback(int current, int total, string filename);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private delegate int DecryptWiiUFilesDelegate(string inputDir, string outputDir, ProgressCallback callback);

        static Wiiu_h3appExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempDllPath = Path.Combine(tempDir, "cdecrypt.dll");

            if (!File.Exists(_tempDllPath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.cdecrypt.dll"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的DLL资源未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempDllPath, buffer);
                }
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            IntPtr hModule = IntPtr.Zero;
            FileSystemWatcher? fileWatcher = null;
            List<string> extractedFiles = new List<string>();

            try
            {
                ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

                var originalFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();

                fileWatcher = new FileSystemWatcher
                {
                    Path = directoryPath,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
                };

                fileWatcher.Created += (sender, e) =>
                {
                    if (!originalFiles.Contains(e.FullPath) &&
                        !e.FullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (extractedFiles)
                        {
                            if (!extractedFiles.Contains(e.FullPath))
                            {
                                extractedFiles.Add(e.FullPath);
                                TotalFilesToExtract = extractedFiles.Count;
                                ExtractionProgress?.Invoke(this, $"检测到新文件({extractedFiles.Count}): {Path.GetFileName(e.FullPath)}");
                                OnFileExtracted(e.FullPath);
                            }
                        }
                    }
                };

                hModule = LoadLibrary(_tempDllPath);
                if (hModule == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    ExtractionError?.Invoke(this, $"加载DLL失败,错误码:{error}");
                    OnExtractionFailed($"加载DLL失败,错误码:{error}");
                    return;
                }

                IntPtr pProc = GetProcAddress(hModule, "DecryptWiiUFiles");
                if (pProc == IntPtr.Zero)
                {
                    ExtractionError?.Invoke(this, "找不到函数入口DecryptWiiUFiles");
                    OnExtractionFailed("找不到函数入口DecryptWiiUFiles");
                    return;
                }

                var decryptFunc = (DecryptWiiUFilesDelegate)Marshal.GetDelegateForFunctionPointer(pProc, typeof(DecryptWiiUFilesDelegate));

                ProgressCallback callback = (current, total, filename) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ExtractionProgress?.Invoke(this, $"进度:{current}/{total}");
                };

                int result = await Task.Run(() => decryptFunc(directoryPath, directoryPath, callback), cancellationToken);

                if (result != 0)
                {
                    ExtractionError?.Invoke(this, $"解密处理失败,返回码:{result}");
                    OnExtractionFailed($"解密处理失败,返回码:{result}");
                    return;
                }

                await Task.Delay(1000, cancellationToken);

                var finalFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                    .Except(originalFiles)
                    .ToList();

                lock (extractedFiles)
                {
                    foreach (var file in finalFiles)
                    {
                        if (!extractedFiles.Contains(file))
                        {
                            extractedFiles.Add(file);
                        }
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成,提取出{extractedFiles.Count}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理失败:{ex.Message}");
                OnExtractionFailed($"处理失败:{ex.Message}");
            }
            finally
            {
                if (fileWatcher != null)
                {
                    fileWatcher.EnableRaisingEvents = false;
                    fileWatcher.Dispose();
                }

                if (hModule != IntPtr.Zero)
                {
                    FreeLibrary(hModule);
                }
            }
        }
    }
}
