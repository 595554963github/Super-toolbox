using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class Minlz_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        static Minlz_Decompressor()
        {
            _tempExePath = ExtractMinlzToTemp();
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
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
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsMinlzFile).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的MinLZ压缩文件");
                        OnDecompressionFailed("未找到有效的MinLZ压缩文件");
                        return;
                    }
                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压，共{TotalFilesToDecompress}个文件");
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DecompressMinlzFile(filePath, decompressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string originalExtension = GetOriginalExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, fileName + originalExtension);
                            string displayName = Path.GetFileName(outputPath);
                            OnFileDecompressed(outputPath);
                        }
                    }
                    OnDecompressionCompleted();
                    DecompressionProgress?.Invoke(this, $"解压完成，共解压{TotalFilesToDecompress}个文件");
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
        private bool IsMinlzFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".mz";
        }
        private string GetOriginalExtension(string compressedFilePath)
        {
            return "";
        }
        private bool DecompressMinlzFile(string inputPath, string outputDir)
        {
            try
            {
                string workingDirectory = Path.GetDirectoryName(inputPath) ?? string.Empty;
                string fileName = Path.GetFileName(inputPath);

                if (string.IsNullOrEmpty(workingDirectory))
                {
                    DecompressionError?.Invoke(this, "无法确定工作目录");
                    return false;
                }
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"d \"{fileName}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        DecompressionError?.Invoke(this, $"MinLZ解压错误:{error}");
                        return false;
                    }
                    string decompressedFileName = Path.GetFileNameWithoutExtension(fileName);
                    string decompressedFile = Path.Combine(workingDirectory, decompressedFileName);
                    if (File.Exists(decompressedFile))
                    {
                        string finalOutputPath = Path.Combine(outputDir, Path.GetFileName(decompressedFile));
                        File.Move(decompressedFile, finalOutputPath, true);
                        return true;
                    }
                    string decompressedFileWithExt = decompressedFile + GetOriginalExtension(inputPath);
                    if (File.Exists(decompressedFileWithExt))
                    {
                        string finalOutputPath = Path.Combine(outputDir, Path.GetFileName(decompressedFileWithExt));
                        File.Move(decompressedFileWithExt, finalOutputPath, true);
                        return true;
                    }
                    DecompressionError?.Invoke(this, $"找不到解压后的文件:{decompressedFileName}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"MinLZ解压错误:{ex.Message}");
                return false;
            }
        }
        private static string ExtractMinlzToTemp()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "super_toolbox_minlz.exe");
            if (File.Exists(tempPath))
            {
                return tempPath;
            }
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("minlz.exe"));
            if (string.IsNullOrEmpty(resourceName))
            {
                throw new FileNotFoundException("找不到嵌入的minlz.exe资源");
            }
            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                    throw new FileNotFoundException("无法读取嵌入的minlz.exe资源");
                using (var fileStream = File.Create(tempPath))
                {
                    resourceStream.CopyTo(fileStream);
                }
            }
            return tempPath;
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}