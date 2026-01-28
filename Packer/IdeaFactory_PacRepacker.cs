using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace super_toolbox
{
    public class IdeaFactory_PacRepacker : BaseExtractor
    {
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        private static bool _dllLoaded = false;
        private static readonly object _lock = new object();

        static IdeaFactory_PacRepacker()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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
        private static extern bool PackDirectoryToPAC([MarshalAs(UnmanagedType.LPWStr)] string inputDir,
                                                    [MarshalAs(UnmanagedType.LPWStr)] string pacFile);

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
                PackingError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnPackingFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
            if (allFiles.Count == 0)
            {
                PackingError?.Invoke(this, "未找到可打包的文件");
                OnPackingFailed("未找到可打包的文件");
                return;
            }

            TotalFilesToPack = allFiles.Count;
            PackingStarted?.Invoke(this, $"开始打包{allFiles.Count}个文件到PAC文件");
            PackingProgress?.Invoke(this, "要打包的文件列表:");
            foreach (var file in allFiles)
            {
                string relativePath = GetRelativePath(directoryPath, file);
                PackingProgress?.Invoke(this, $"{relativePath}");
            }

            try
            {
                await CreateSinglePacFile(directoryPath, allFiles, cancellationToken);
                OnPackingCompleted();
            }
            catch (OperationCanceledException)
            {
                PackingError?.Invoke(this, "打包操作已取消");
                OnPackingFailed("打包操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"打包失败:{ex.Message}");
                OnPackingFailed($"打包失败:{ex.Message}");
            }
        }

        private async Task CreateSinglePacFile(string baseDirectory, System.Collections.Generic.List<string> allFiles, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string baseDirName = Path.GetFileName(baseDirectory);
            if (string.IsNullOrEmpty(baseDirName))
            {
                baseDirName = "packed";
            }

            string parentDirectory = Directory.GetParent(baseDirectory)?.FullName ?? baseDirectory;
            string pacFileName = baseDirName + ".pac";
            string outputPath = Path.Combine(parentDirectory, pacFileName);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            PackingProgress?.Invoke(this, $"正在创建PAC文件:{pacFileName},包含{allFiles.Count}个文件");

            foreach (var sourceFile in allFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                OnFilePacked(sourceFile);
                PackingProgress?.Invoke(this, $"正在打包:{Path.GetFileName(sourceFile)}");
            }

            bool success = PackDirectoryToPAC(baseDirectory, outputPath);

            if (success)
            {
                if (File.Exists(outputPath))
                {
                    FileInfo fileInfo = new FileInfo(outputPath);
                    PackingProgress?.Invoke(this, $"打包完成:{Path.GetFileName(outputPath)} ({FormatFileSize(fileInfo.Length)})");
                }
                else
                {
                    throw new FileNotFoundException("打包过程未生成PAC文件", pacFileName);
                }
            }
            else
            {
                string errorMsg = GetLastErrorMessage();
                throw new Exception($"打包失败:{errorMsg}");
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar);

            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }
    }
}
