using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class Yaz0_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        private static readonly byte[] Yaz0Magic = { 0x59, 0x61, 0x7A, 0x30 };
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;
        static Yaz0_Decompressor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "yaz0.exe");
            if (!File.Exists(_tempExePath))
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith("yaz0.exe"));
                if (string.IsNullOrEmpty(resourceName))
                    throw new FileNotFoundException("嵌入的Yaz0解压工具资源未找到");
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException("无法读取嵌入的Yaz0解压工具资源");
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
                    var filesToProcess = allFiles.Where(IsYaz0File).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到包含Yaz0数据的文件");
                        OnDecompressionFailed("未找到包含Yaz0数据的文件");
                        return;
                    }
                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);
                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压，共{TotalFilesToDecompress}个文件");
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (DecompressYaz0File(filePath, decompressedDir))
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
        private bool IsYaz0File(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[50];
                    int bytesRead = fs.Read(buffer, 0, 50);
                    for (int i = 0; i <= bytesRead - Yaz0Magic.Length; i++)
                    {
                        if (buffer.Skip(i).Take(Yaz0Magic.Length).SequenceEqual(Yaz0Magic))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
        private string GetOriginalExtension(string compressedFilePath)
        {
            return "";
        }
        private bool DecompressYaz0File(string inputPath, string outputDir)
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            bool useTempFile = false;
            try
            {
                int yaz0Start = FindYaz0StartPosition(inputPath);
                if (yaz0Start < 0)
                {
                    DecompressionError?.Invoke(this, $"未找到Yaz0数据:{Path.GetFileName(inputPath)}");
                    return false;
                }
                if (yaz0Start > 0)
                {
                    useTempFile = true;
                    using (var inStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
                    using (var outStream = new FileStream(tempFilePath, FileMode.Create))
                    {
                        inStream.Seek(yaz0Start, SeekOrigin.Begin);
                        inStream.CopyTo(outStream);
                    }
                }
                else
                {
                    tempFilePath = inputPath;
                }
                string workingDirectory = Path.GetDirectoryName(inputPath) ?? string.Empty;
                string fileName = Path.GetFileName(inputPath);
                string decompressedFileName = Path.GetFileNameWithoutExtension(fileName);
                string outputPath = Path.Combine(outputDir, decompressedFileName);
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"-d \"{tempFilePath}\" -o \"{outputPath}\"",
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
                        DecompressionError?.Invoke(this, $"Yaz0解压错误:{error}");
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
                DecompressionError?.Invoke(this, $"Yaz0解压错误:{ex.Message}");
                return false;
            }
            finally
            {
                if (useTempFile && File.Exists(tempFilePath) && tempFilePath != inputPath)
                {
                    try
                    {
                        File.Delete(tempFilePath);
                    }
                    catch
                    {
                    }
                }
            }
        }
        private int FindYaz0StartPosition(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[50];
                int bytesRead = fs.Read(buffer, 0, 50);
                for (int i = 0; i <= bytesRead - Yaz0Magic.Length; i++)
                {
                    if (buffer.Skip(i).Take(Yaz0Magic.Length).SequenceEqual(Yaz0Magic))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}