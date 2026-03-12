using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace super_toolbox
{
    public class XWBPacker : BaseExtractor
    {
        private static string _tempDllPath;
        public event EventHandler<string>? PackingStarted;
        public event EventHandler<string>? PackingProgress;
        public event EventHandler<string>? PackingError;

        [DllImport("kernel32.dll", SetLastError = true)]
        private new static extern bool SetDllDirectory(string lpPathName);

        [DllImport("XWBTool.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int XWBTool_CreateWaveBank(
            string outputFile,
            string[] inputFiles,
            int fileCount,
            int streaming,
            int advancedFormat,
            int forceCompact,
            int includeFriendlyNames,
            StringBuilder errorMessage,
            int errorMessageSize);

        static XWBPacker()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempDllPath = Path.Combine(tempDir, "XWBTool.dll");

            if (!File.Exists(_tempDllPath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.XWBTool.dll"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的XWBTool.dll资源未找到");
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempDllPath, buffer);
                }
            }

            SetDllDirectory(tempDir);
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await PackWavFilesToParentDirectoryAsync(directoryPath, cancellationToken);
        }

        public async Task PackWavFilesToParentDirectoryAsync(string inputDirectory, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(inputDirectory))
            {
                PackingError?.Invoke(this, "错误:输入目录不存在");
                OnPackingFailed("错误:输入目录不存在");
                return;
            }

            var wavFiles = Directory.GetFiles(inputDirectory, "*.wav", SearchOption.AllDirectories);
            if (wavFiles.Length == 0)
            {
                PackingError?.Invoke(this, "未找到.wav文件");
                OnPackingFailed("未找到.wav文件");
                return;
            }

            TotalFilesToPack = wavFiles.Length;
            PackingStarted?.Invoke(this, $"开始打包{wavFiles.Length}个WAV文件到XWB文件");
            PackingProgress?.Invoke(this, "要打包的WAV文件列表:");

            foreach (var file in wavFiles)
            {
                string fileName = Path.GetFileName(file);
                FileInfo fileInfo = new FileInfo(file);
                PackingProgress?.Invoke(this, $"准备添加:{fileName} ({FormatFileSize(fileInfo.Length)})");
            }

            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string folderName = Path.GetFileName(inputDirectory.TrimEnd(Path.DirectorySeparatorChar));
                    if (string.IsNullOrEmpty(folderName))
                    {
                        folderName = "output";
                    }

                    string? parentDirectory = Directory.GetParent(inputDirectory)?.FullName;
                    if (string.IsNullOrEmpty(parentDirectory))
                    {
                        PackingError?.Invoke(this, "无法确定父目录路径");
                        OnPackingFailed("无法确定父目录路径");
                        return;
                    }

                    string outputPath = Path.Combine(parentDirectory, $"{folderName}.xwb");
                    PackingProgress?.Invoke(this, $"输出文件路径:{outputPath}");

                    if (File.Exists(outputPath))
                    {
                        PackingProgress?.Invoke(this, "删除已存在的输出文件");
                        File.Delete(outputPath);
                    }

                    StringBuilder errorMessage = new StringBuilder(1024);

                    PackingProgress?.Invoke(this, $"正在打包XWB文件:{Path.GetFileName(outputPath)}");

                    int result = XWBTool_CreateWaveBank(
                        outputPath,
                        wavFiles,
                        wavFiles.Length,
                        0,
                        0,
                        0,
                        1,
                        errorMessage,
                        errorMessage.Capacity);

                    if (result != 0)
                    {
                        string error = errorMessage.Length > 0 ? errorMessage.ToString() : "未知错误";
                        throw new Exception($"XWB打包失败，错误代码:{result}，详情:{error}");
                    }

                    if (File.Exists(outputPath))
                    {
                        FileInfo fileInfo = new FileInfo(outputPath);
                        PackingProgress?.Invoke(this, "打包完成!");
                        PackingProgress?.Invoke(this, $"输出文件:{Path.GetFileName(outputPath)}");
                        PackingProgress?.Invoke(this, $"文件大小:{FormatFileSize(fileInfo.Length)}");
                        PackingProgress?.Invoke(this, $"包含WAV文件数:{wavFiles.Length}");

                        foreach (var file in wavFiles)
                        {
                            OnFilePacked(file);
                        }
                        OnPackingCompleted();
                    }
                    else
                    {
                        throw new FileNotFoundException("XWB打包过程未生成输出文件", outputPath);
                    }
                }, cancellationToken);
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

        public void PackWavFilesToParentDirectory(string inputDirectory)
        {
            PackWavFilesToParentDirectoryAsync(inputDirectory).Wait();
        }

        public override void Extract(string directoryPath)
        {
            PackWavFilesToParentDirectory(directoryPath);
        }
    }
}