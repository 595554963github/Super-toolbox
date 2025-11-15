using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class LzssCustom_Compressor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;
        static LzssCustom_Compressor()
        {
            _tempExePath = ExtractLzssToTemp();
        }
        private static string ExtractLzssToTemp()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "super_toolbox_lzss.exe");
            if (File.Exists(tempPath))
            {
                return tempPath;
            }
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("sample.exe"));
            if (string.IsNullOrEmpty(resourceName))
            {
                throw new FileNotFoundException("找不到嵌入的LZSS压缩工具资源");
            }
            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                    throw new FileNotFoundException("无法读取嵌入的LZSS压缩工具资源");
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
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsNotLzssFile).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        CompressionError?.Invoke(this, "未找到需要压缩的文件");
                        OnCompressionFailed("未找到需要压缩的文件");
                        return;
                    }
                    string compressedDir = Path.Combine(directoryPath, "Compressed");
                    Directory.CreateDirectory(compressedDir);
                    TotalFilesToCompress = filesToProcess.Length;
                    CompressionStarted?.Invoke(this, $"开始压缩，共{TotalFilesToCompress}个文件");
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (CompressLzssFile(filePath, compressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputPath = Path.Combine(compressedDir, fileName + ".lzss");
                            OnFileCompressed(outputPath);
                        }
                    }
                    OnCompressionCompleted();
                    CompressionProgress?.Invoke(this, $"压缩完成，共压缩{TotalFilesToCompress}个文件");
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
        private bool IsNotLzssFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension != ".lzss";
        }
        private bool CompressLzssFile(string inputPath, string outputDir)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(inputPath);
                string originalExtension = Path.GetExtension(inputPath);
                string tempOutputPath = Path.Combine(outputDir, fileName + ".lzss.temp");
                string finalOutputPath = Path.Combine(outputDir, fileName + ".lzss");
                CompressionProgress?.Invoke(this, $"正在压缩:{Path.GetFileName(inputPath)}");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"-c -i \"{inputPath}\" -o \"{tempOutputPath}\"",
                        WorkingDirectory = Path.GetDirectoryName(inputPath) ?? string.Empty,
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
                    CompressionError?.Invoke(this, $"LZSS压缩错误:{error}");
                    if (File.Exists(tempOutputPath)) File.Delete(tempOutputPath);
                    return false;
                }
                if (File.Exists(tempOutputPath))
                {
                    AddMetadataToCompressedFile(tempOutputPath, finalOutputPath, originalExtension);
                    File.Delete(tempOutputPath);
                    CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(finalOutputPath)}");
                    return true;
                }
                else
                {
                    CompressionError?.Invoke(this, $"压缩成功但输出文件不存在:{tempOutputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"LZSS压缩错误:{ex.Message}");
                return false;
            }
        }
        private void AddMetadataToCompressedFile(string tempFilePath, string finalFilePath, string originalExtension)
        {
            byte[] compressedData = File.ReadAllBytes(tempFilePath);
            byte[] extensionBytes = Encoding.UTF8.GetBytes(originalExtension);

            using (var fileStream = new FileStream(finalFilePath, FileMode.Create))
            using (var writer = new BinaryWriter(fileStream))
            {
                writer.Write(Encoding.ASCII.GetBytes("LZSS"));
                writer.Write((byte)extensionBytes.Length);
                writer.Write(extensionBytes);
                writer.Write(compressedData);
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}