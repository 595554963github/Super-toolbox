using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class CSO_PakExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempDllPath;
        private static bool _dllLoaded = false;

        static CSO_PakExtractor()
        {
            _tempDllPath = Path.Combine(Path.GetTempPath(), "supertoolbox_temp", "csopak.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(_tempDllPath)!);
        }

        private static void LoadCSOPakDll()
        {
            if (_dllLoaded) return;

            if (!File.Exists(_tempDllPath))
            {
                ExtractEmbeddedResource("embedded.csopak.dll", _tempDllPath);
            }

            NativeLibrary.Load(_tempDllPath);
            _dllLoaded = true;
        }

        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException($"嵌入的CSOPAK资源未找到:{resourceName}");

            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            File.WriteAllBytes(outputPath, buffer);
        }

        [DllImport("csopak.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int csopak_initialize();

        [DllImport("csopak.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int csopak_cleanup();

        [DllImport("csopak.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr csopak_open_file([MarshalAs(UnmanagedType.LPStr)] string file_path, out int error_code);

        [DllImport("csopak.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int csopak_close_file(IntPtr pak_handle);

        [DllImport("csopak.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int csopak_get_file_count(IntPtr pak_handle, out uint count);

        [DllImport("csopak.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int csopak_extract_all(IntPtr pak_handle, [MarshalAs(UnmanagedType.LPStr)] string output_dir);

        [StructLayout(LayoutKind.Sequential)]
        private struct CSOPakFileInfo
        {
            public IntPtr file_path;
            public uint real_size;
            public uint packed_size;
            public uint offset;
            public uint type;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var pakFiles = Directory.GetFiles(directoryPath, "*.pak");
            if (pakFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.pak文件");
                OnExtractionFailed("未找到.pak文件");
                return;
            }

            TotalFilesToExtract = pakFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{pakFiles.Length}个PAK文件");

            try
            {
                LoadCSOPakDll();

                int initResult = csopak_initialize();
                if (initResult != 0)
                {
                    ExtractionError?.Invoke(this, $"CSOPAK初始化失败:{initResult}");
                    OnExtractionFailed($"CSOPAK初始化失败:{initResult}");
                    return;
                }

                await Task.Run(() =>
                {
                    int totalExtractedFiles = 0;

                    foreach (var pakFilePath in pakFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(pakFilePath)}");

                        try
                        {
                            int openError;
                            IntPtr pakHandle = csopak_open_file(pakFilePath, out openError);

                            if (openError != 0 || pakHandle == IntPtr.Zero)
                            {
                                ExtractionError?.Invoke(this, $"打开PAK文件失败:{openError}");
                                continue;
                            }

                            string workingDir = Path.GetDirectoryName(pakFilePath) ?? directoryPath;

                            int extractResult = csopak_extract_all(pakHandle, workingDir);

                            if (extractResult == 0)
                            {
                                var extractedFiles = Directory.GetFiles(workingDir, "*.*", SearchOption.AllDirectories)
                                    .Where(f => !f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
                                    .ToArray();

                                totalExtractedFiles += extractedFiles.Length;

                                foreach (var file in extractedFiles)
                                {
                                    string fileName = Path.GetFileName(file);
                                    OnFileExtracted(fileName);
                                    ExtractionProgress?.Invoke(this, $"已提取:{fileName}");
                                }

                                ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(pakFilePath)} -> {extractedFiles.Length}个文件");
                            }
                            else
                            {
                                ExtractionError?.Invoke(this, $"提取文件{Path.GetFileName(pakFilePath)}失败,错误代码:{extractResult}");
                            }

                            csopak_close_file(pakHandle);
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"文件{Path.GetFileName(pakFilePath)}处理错误:{ex.Message}");
                            OnExtractionFailed($"文件{Path.GetFileName(pakFilePath)} 处理错误:{ex.Message}");
                        }
                    }

                    csopak_cleanup();
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                csopak_cleanup();
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                csopak_cleanup();
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
