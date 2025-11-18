using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class Wflz_Compressor : BaseExtractor
    {
        private static string _tempExePath;
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        static Wflz_Compressor()
        {
            _tempExePath = ExtractWflzToTemp();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(file => !file.EndsWith(".wflz", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (filesToCompress.Length == 0)
            {
                CompressionError?.Invoke(this, "未找到需要压缩的文件");
                OnCompressionFailed("未找到需要压缩的文件");
                return;
            }

            string compressedDir = Path.Combine(directoryPath, "Compressed");
            Directory.CreateDirectory(compressedDir);

            CompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(compressedDir, "*.wflz", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToCompress = filesToCompress.Length;
                    int processedFiles = 0;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;

                        CompressionProgress?.Invoke(this, $"正在压缩文件({processedFiles}/{TotalFilesToCompress}): {Path.GetFileName(filePath)}");

                        string relativePath = GetRelativePath(directoryPath, filePath);
                        string outputPath = Path.Combine(compressedDir, relativePath + ".wflz");
                        string outputDir = Path.GetDirectoryName(outputPath) ??
                            throw new InvalidOperationException($"无法确定输出目录路径:{outputPath}");

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        if (CompressWflzFile(filePath, outputPath))
                        {
                            CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)}");
                            OnFileCompressed(outputPath);
                        }
                        else
                        {
                            CompressionError?.Invoke(this, $"压缩失败:{Path.GetFileName(filePath)}");
                            OnCompressionFailed($"压缩失败:{Path.GetFileName(filePath)}");
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
                CompressionError?.Invoke(this, $"压缩过程出错:{ex.Message}");
                OnCompressionFailed($"压缩过程出错:{ex.Message}");
            }
        }

        private bool CompressWflzFile(string inputPath, string outputPath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"c \"{inputPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    string expectedOutput = inputPath + ".wflz";
                    if (File.Exists(expectedOutput))
                    {
                        if (!expectedOutput.Equals(outputPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(outputPath))
                            {
                                File.Delete(outputPath);
                            }
                            File.Move(expectedOutput, outputPath);
                        }
                        return true;
                    }
                    else
                    {
                        CompressionError?.Invoke(this, $"压缩成功但未找到输出文件: {expectedOutput}");
                        return false;
                    }
                }
                else
                {
                    CompressionError?.Invoke(this, $"压缩失败({Path.GetFileName(inputPath)}): {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"压缩过程异常({Path.GetFileName(inputPath)}): {ex.Message}");
                return false;
            }
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
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