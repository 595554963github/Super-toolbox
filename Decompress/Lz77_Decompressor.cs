using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class Lz77_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;
        private static string _tempRuntimeConfigPath;
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        static Lz77_Decompressor()
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
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsLz77File).ToArray();
                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的LZ77压缩文件");
                        OnDecompressionFailed("未找到有效的LZ77压缩文件");
                        return;
                    }
                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压，共{TotalFilesToDecompress}个文件");
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DecompressLz77File(filePath, decompressedDir))
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
        private bool IsLz77File(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lz77";
        }
        private bool DecompressLz77File(string inputPath, string outputDir)
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
                        Arguments = $"-d \"{inputPath}\" -o \"{outputPath}\"",
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
                    DecompressionError?.Invoke(this, $"LZ77解压错误:{error}");
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
                DecompressionError?.Invoke(this, $"LZ77解压错误:{ex.Message}");
                return false;
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}