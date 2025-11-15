using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class LzssCustom_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        static LzssCustom_Decompressor()
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
                throw new FileNotFoundException("找不到嵌入的LZSS解压工具资源");
            }
            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                    throw new FileNotFoundException("无法读取嵌入的LZSS解压工具资源");
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
                    var filesToProcess = allFiles.Where(IsLzssFile).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到需要解压的LZSS文件");
                        OnDecompressionFailed("未找到需要解压的LZSS文件");
                        return;
                    }
                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压，共{TotalFilesToDecompress}个文件");
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DecompressLzssFile(filePath, decompressedDir))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string originalExtension = ExtractOriginalExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, fileName + originalExtension);
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
        private bool IsLzssFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lzss";
        }
        private bool DecompressLzssFile(string inputPath, string outputDir)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(inputPath);
                string originalExtension = ExtractOriginalExtension(inputPath);
                string tempLzssPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".lzss");
                string tempOutputPath = Path.Combine(outputDir, fileName + ".temp");
                string finalOutputPath = Path.Combine(outputDir, fileName + originalExtension);
                DecompressionProgress?.Invoke(this, $"正在解压:{Path.GetFileName(inputPath)}");
                ExtractCompressedData(inputPath, tempLzssPath);
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"-d -i \"{tempLzssPath}\" -o \"{tempOutputPath}\"",
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
                if (File.Exists(tempLzssPath)) File.Delete(tempLzssPath);
                if (process.ExitCode != 0)
                {
                    DecompressionError?.Invoke(this, $"LZSS解压错误:{error}");
                    return false;
                }
                if (File.Exists(tempOutputPath))
                {
                    File.Move(tempOutputPath, finalOutputPath);
                    DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(finalOutputPath)}");
                    return true;
                }
                else
                {
                    DecompressionError?.Invoke(this, $"解压成功但输出文件不存在:{tempOutputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"LZSS解压错误:{ex.Message}");
                return false;
            }
        }
        private string ExtractOriginalExtension(string lzssFilePath)
        {
            try
            {
                using (var fileStream = new FileStream(lzssFilePath, FileMode.Open))
                using (var reader = new BinaryReader(fileStream))
                {
                    byte[] magic = reader.ReadBytes(4);
                    if (Encoding.ASCII.GetString(magic) != "LZSS")
                    {
                        return "";
                    }
                    byte extensionLength = reader.ReadByte();
                    byte[] extensionBytes = reader.ReadBytes(extensionLength);
                    return Encoding.UTF8.GetString(extensionBytes);
                }
            }
            catch
            {
                return "";
            }
        }
        private void ExtractCompressedData(string inputPath, string outputPath)
        {
            using (var inputStream = new FileStream(inputPath, FileMode.Open))
            using (var reader = new BinaryReader(inputStream))
            using (var outputStream = new FileStream(outputPath, FileMode.Create))
            using (var writer = new BinaryWriter(outputStream))
            {
                reader.BaseStream.Seek(4, SeekOrigin.Current); 
                byte extensionLength = reader.ReadByte();
                reader.BaseStream.Seek(extensionLength, SeekOrigin.Current);
                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    writer.Write(buffer, 0, bytesRead);
                }
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}