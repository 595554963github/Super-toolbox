using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;

namespace super_toolbox
{
    public class Minlz_Compressor : BaseExtractor
    {
        private string _minlzPath;

        public Minlz_Compressor()
        {
            _minlzPath = ExtractMinlzToTemp();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*");
            if (filesToCompress.Length == 0)
            {
                OnExtractionFailed("未找到需要压缩的文件");
                return;
            }

            string compressedDir = Path.Combine(directoryPath, "Compressed");
            Directory.CreateDirectory(compressedDir);

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(compressedDir, "*.mz"))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToExtract = filesToCompress.Length;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (CompressFileWithMinlz(filePath))
                        {
                            string compressedFile = filePath + ".mz";
                            if (File.Exists(compressedFile))
                            {
                                string fileName = Path.GetFileName(compressedFile);
                                string outputPath = Path.Combine(compressedDir, fileName);
                                File.Move(compressedFile, outputPath);
                                OnFileExtracted(outputPath);
                            }
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"压缩失败: {ex.Message}");
            }
        }

        private bool CompressFileWithMinlz(string inputPath)
        {
            try
            {
                string? workingDirectory = Path.GetDirectoryName(inputPath);
                string fileName = Path.GetFileName(inputPath);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _minlzPath,
                    Arguments = $"c \"{fileName}\"",
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
                        OnExtractionFailed($"MinLZ压缩错误: {error}");
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"MinLZ压缩错误: {ex.Message}");
                return false;
            }
        }

        private string ExtractMinlzToTemp()
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