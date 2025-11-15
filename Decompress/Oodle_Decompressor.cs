using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class Oodle_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        static Oodle_Decompressor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "ooz.exe");
            if (!File.Exists(_tempExePath))
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith("ooz.exe"));
                if (string.IsNullOrEmpty(resourceName))
                    throw new FileNotFoundException("嵌入的Oodle解压工具资源未找到");
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException("无法读取嵌入的Oodle解压工具资源");
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(_tempExePath, buffer);
                }
            }
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
                    var filesToProcess = allFiles.Where(IsOodleFile).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的Oodle压缩文件");
                        OnDecompressionFailed("未找到有效的Oodle压缩文件");
                        return;
                    }
                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压，共{TotalFilesToDecompress}个文件");
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DecompressOodleFile(filePath, decompressedDir))
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
        private bool IsOodleFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".ozip";
        }
        private string GetOriginalExtension(string compressedFilePath)
        {
            return "";
        }
        private bool DecompressOodleFile(string inputPath, string outputDir)
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
                string decompressedFileName = Path.GetFileNameWithoutExtension(fileName);
                string outputPath = Path.Combine(outputDir, decompressedFileName);
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"-d \"{inputPath}\" \"{outputPath}\"",
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
                        DecompressionError?.Invoke(this, $"Oodle解压错误:{error}");
                        return false;
                    }
                    if (File.Exists(outputPath))
                    {
                        return true;
                    }
                    DecompressionError?.Invoke(this, $"找不到解压后的文件:{decompressedFileName}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"Oodle解压错误:{ex.Message}");
                return false;
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}