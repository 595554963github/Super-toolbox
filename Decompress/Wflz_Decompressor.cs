using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class Wflz_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        static Wflz_Decompressor()
        {
            _tempExePath = ExtractWflzToTemp();
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
                    var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
                    var filesToProcess = allFiles.Where(IsWflzFile).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的WFLZ压缩文件");
                        OnDecompressionFailed("未找到有效的WFLZ压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);

                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压，共{TotalFilesToDecompress}个文件");

                    int processedFiles = 0;
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;

                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        if (DecompressWflzFile(filePath, decompressedDir))
                        {
                            string originalFileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, originalFileName);

                            OnFileDecompressed(outputPath);
                            DecompressionProgress?.Invoke(this, $"已解压:{originalFileName}");
                        }
                        else
                        {
                            DecompressionError?.Invoke(this, $"解压失败:{Path.GetFileName(filePath)}");
                            OnDecompressionFailed($"解压失败:{Path.GetFileName(filePath)}");
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

        private bool IsWflzFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".wflz";
        }

        private bool DecompressWflzFile(string inputPath, string outputDir)
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
                        DecompressionError?.Invoke(this, $"WFLZ解压错误:{error}");
                        return false;
                    }

                    string decompressedFileName = Path.GetFileNameWithoutExtension(fileName);
                    string decompressedFile = Path.Combine(workingDirectory, decompressedFileName);

                    if (File.Exists(decompressedFile))
                    {
                        string finalOutputPath = Path.Combine(outputDir, Path.GetFileName(decompressedFile));

                        string? finalOutputDir = Path.GetDirectoryName(finalOutputPath);
                        if (!string.IsNullOrEmpty(finalOutputDir) && !Directory.Exists(finalOutputDir))
                        {
                            Directory.CreateDirectory(finalOutputDir);
                        }

                        if (File.Exists(finalOutputPath))
                        {
                            File.Delete(finalOutputPath);
                        }

                        File.Move(decompressedFile, finalOutputPath);
                        return true;
                    }
                    else
                    {
                        DecompressionError?.Invoke(this, $"找不到解压后的文件:{decompressedFileName}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"WFLZ解压错误:{ex.Message}");
                return false;
            }
        }

        private static string ExtractWflzToTemp()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "supertoolbox_temp", "wfLZ.exe");
            string tempDir = Path.GetDirectoryName(tempPath) ?? throw new InvalidOperationException("无法确定临时目录路径");

            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            if (File.Exists(tempPath))
            {
                return tempPath;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("wfLZ.exe"));

            if (string.IsNullOrEmpty(resourceName))
            {
                throw new FileNotFoundException("找不到嵌入的wfLZ.exe资源");
            }

            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                    throw new FileNotFoundException("无法读取嵌入的wfLZ.exe资源");

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