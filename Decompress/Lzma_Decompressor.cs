using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class Lzma_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        private static readonly byte[] LzmaMagicNumber = new byte[] { 0x5D, 0x00, 0x00 };
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        static Lzma_Decompressor()
        {
            _tempExePath = ExtractLzmaToTemp();
        }
        private static string ExtractLzmaToTemp()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "super_toolbox_lzma.exe");
            if (File.Exists(tempPath))
            {
                return tempPath;
            }
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("lzma.exe"));
            if (string.IsNullOrEmpty(resourceName))
            {
                throw new FileNotFoundException("找不到嵌入的lzma.exe资源");
            }
            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                    throw new FileNotFoundException("无法读取嵌入的lzma.exe资源");
                using (var fileStream = File.Create(tempPath))
                {
                    resourceStream.CopyTo(fileStream);
                }
            }
            return tempPath;
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
                    var filesToProcess = allFiles.Where(IsLzmaFile).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的LZMA压缩文件");
                        OnDecompressionFailed("未找到有效的LZMA压缩文件");
                        return;
                    }
                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压，共{TotalFilesToDecompress}个文件");
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DecompressLzmaFile(filePath, decompressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, fileName);
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
        private bool IsLzmaFile(string filePath)
        {
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    if (file.Length < 13) return false;
                    byte[] header = new byte[3];
                    file.Read(header, 0, 3);
                    return header[0] == LzmaMagicNumber[0] &&
                           header[1] == LzmaMagicNumber[1] &&
                           header[2] == LzmaMagicNumber[2];
                }
            }
            catch
            {
                return false;
            }
        }
        private bool DecompressLzmaFile(string inputPath, string outputDir)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(inputPath);
                string outputPath = Path.Combine(outputDir, fileName);
                DecompressionProgress?.Invoke(this, $"正在解压:{Path.GetFileName(inputPath)}");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"d \"{inputPath}\" \"{outputPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    DecompressionError?.Invoke(this, $"LZMA解压错误:{error}");
                    return false;
                }
                if (File.Exists(outputPath))
                {
                    DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)}");
                    return true;
                }
                else
                {
                    DecompressionError?.Invoke(this, $"解压成功但输出文件不存在:{outputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"LZMA解压错误:{ex.Message}");
                return false;
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}