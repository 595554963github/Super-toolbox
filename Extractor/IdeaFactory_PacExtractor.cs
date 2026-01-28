using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace super_toolbox
{
    public class IdeaFactory_PacExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static bool _dllLoaded = false;
        private static readonly object _lock = new object();
        private readonly ConcurrentDictionary<string, bool> _processedFiles = new ConcurrentDictionary<string, bool>();
        private CancellationTokenSource? _monitorCancellationTokenSource;

        static IdeaFactory_PacExtractor()
        {
            LoadPacToolDll();
        }

        private static void LoadPacToolDll()
        {
            lock (_lock)
            {
                if (_dllLoaded) return;

                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
                    Directory.CreateDirectory(tempDir);
                    string dllPath = Path.Combine(tempDir, "pactool.dll");

                    if (!File.Exists(dllPath))
                    {
                        ExtractEmbeddedResource("embedded.pactool.dll", dllPath);
                    }

                    NativeLibrary.Load(dllPath);
                    _dllLoaded = true;
                }
                catch (Exception ex)
                {
                    throw new DllNotFoundException($"无法加载pactool.dll:{ex.Message}", ex);
                }
            }
        }

        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException($"嵌入的资源未找到:{resourceName}");

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                File.WriteAllBytes(outputPath, buffer);
            }
        }

        [DllImport("pactool.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExtractPAC([MarshalAs(UnmanagedType.LPWStr)] string pacFile,
                                            [MarshalAs(UnmanagedType.LPWStr)] string outputDir);

        [DllImport("pactool.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr GetLastErrorMsg();

        private static string GetLastErrorMessage()
        {
            IntPtr ptr = GetLastErrorMsg();
            return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
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

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            var pacFiles = Directory.GetFiles(directoryPath, "*.pac", SearchOption.AllDirectories);
            if (pacFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到任何.pac文件");
                OnExtractionFailed("未找到任何.pac文件");
                return;
            }

            _processedFiles.Clear();
            _monitorCancellationTokenSource = new CancellationTokenSource();

            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _monitorCancellationTokenSource.Token);

            int processedPacFiles = 0;

            try
            {
                foreach (var pacFilePath in pacFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    processedPacFiles++;

                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(pacFilePath)} ({processedPacFiles}/{pacFiles.Length})");

                    try
                    {
                        string fileDirectory = Path.GetDirectoryName(pacFilePath) ?? string.Empty;
                        string pacFileNameWithoutExt = Path.GetFileNameWithoutExtension(pacFilePath);
                        string extractedFolder = Path.Combine(fileDirectory, pacFileNameWithoutExt);

                        if (Directory.Exists(extractedFolder))
                        {
                            Directory.Delete(extractedFolder, true);
                        }

                        Directory.CreateDirectory(extractedFolder);

                        var monitorTask = StartDirectoryMonitor(extractedFolder, linkedTokenSource.Token);

                        await Task.Run(() =>
                        {
                            bool success = ExtractPAC(pacFilePath, extractedFolder);

                            if (!success)
                            {
                                string errorMsg = GetLastErrorMessage();
                                throw new Exception($"{Path.GetFileName(pacFilePath)}解包失败:{errorMsg}");
                            }
                        }, cancellationToken);

                        await Task.Delay(500);

                        _monitorCancellationTokenSource!.Cancel();


                        try
                        {
                            await monitorTask;
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception)
                        {
                        }

                        _monitorCancellationTokenSource.Dispose();
                        _monitorCancellationTokenSource = null;

                        var allExtractedFiles = Directory.GetFiles(extractedFolder, "*", SearchOption.AllDirectories);
                        ExtractionProgress?.Invoke(this, $"{Path.GetFileName(pacFilePath)}解包完成,共提取了{allExtractedFiles.Length}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{pacFilePath}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{pacFilePath}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"处理完成，总提取文件数:{ExtractedFileCount}");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            finally
            {
                if (_monitorCancellationTokenSource != null)
                {
                    _monitorCancellationTokenSource.Cancel();
                    _monitorCancellationTokenSource.Dispose();
                }
            }
        }

        private async Task StartDirectoryMonitor(string directoryPath, CancellationToken cancellationToken)
        {
            try
            {
                HashSet<string> lastFiles = new HashSet<string>();

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!Directory.Exists(directoryPath))
                        {
                            await Task.Delay(500, cancellationToken);
                            continue;
                        }

                        var currentFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

                        foreach (var file in currentFiles)
                        {
                            if (!lastFiles.Contains(file) && !_processedFiles.ContainsKey(file))
                            {
                                _processedFiles[file] = true;
                                OnFileExtracted(file);
                                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(file)}");
                            }
                        }

                        lastFiles = new HashSet<string>(currentFiles);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }

                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
