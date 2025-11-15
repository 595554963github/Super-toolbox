using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class Lz77_Compressor : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;
        private static string _tempRuntimeConfigPath;
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;
        static Lz77_Compressor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "LZ77.exe");
            _tempDllPath = Path.Combine(tempDir, "LZ77.dll");
            _tempRuntimeConfigPath = Path.Combine(tempDir, "LZ77.runtimeconfig.json");
            ExtractEmbeddedResource("embedded.LZ77.exe", _tempExePath);
            ExtractEmbeddedResource("embedded.LZ77.dll", _tempDllPath);
            ExtractEmbeddedResource("embedded.LZ77.runtimeconfig.json", _tempRuntimeConfigPath);
        }
        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            if (!File.Exists(outputPath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException($"嵌入的LZ77资源未找到:{resourceName}");
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(outputPath, buffer);
                }
            }
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
                    var filesToProcess = allFiles.Where(IsNotLz77File).ToArray();
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
                        if (CompressLz77File(filePath, compressedDir))
                        {
                            string fileName = Path.GetFileName(filePath);
                            string outputPath = Path.Combine(compressedDir, fileName + ".lz77");
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
        private bool IsNotLz77File(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension != ".lz77";
        }
        private bool CompressLz77File(string inputPath, string outputDir)
        {
            try
            {
                string fileName = Path.GetFileName(inputPath);
                string outputPath = Path.Combine(outputDir, fileName + ".lz77");
                CompressionProgress?.Invoke(this, $"正在压缩:{fileName}");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"-c \"{inputPath}\" -o \"{outputPath}\"",
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
                    CompressionError?.Invoke(this, $"LZ77压缩错误:{error}");
                    return false;
                }
                if (File.Exists(outputPath))
                {
                    CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)}");
                    return true;
                }
                else
                {
                    CompressionError?.Invoke(this, $"压缩成功但输出文件不存在:{outputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"LZ77压缩错误:{ex.Message}");
                return false;
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}