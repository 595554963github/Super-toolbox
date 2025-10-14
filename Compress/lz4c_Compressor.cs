using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;

namespace super_toolbox
{
    public class Lz4c_Compressor : BaseExtractor
    {
        private string _lz4cPath;

        public Lz4c_Compressor()
        {
            _lz4cPath = ExtractLz4cToTemp();
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
                    foreach (var file in Directory.GetFiles(compressedDir, "*.lz4"))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToExtract = filesToCompress.Length;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (CompressFileWithLz4c(filePath))
                        {
                            string compressedFile = filePath + ".lz4";
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

        private bool CompressFileWithLz4c(string inputPath)
        {
            try
            {
                string workingDirectory = Path.GetDirectoryName(inputPath) ?? string.Empty;
                string fileName = Path.GetFileName(inputPath);

                if (string.IsNullOrEmpty(workingDirectory))
                {
                    OnExtractionFailed("无法确定工作目录");
                    return false;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _lz4cPath,
                    Arguments = $"compress \"{fileName}\"",
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
                        OnExtractionFailed($"LZ4C压缩错误: {error}");
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"LZ4C压缩错误: {ex.Message}");
                return false;
            }
        }

        private string ExtractLz4cToTemp()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "super_toolbox_lz4c.exe");

            if (File.Exists(tempPath))
            {
                return tempPath;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("lz4c.exe"));

            if (string.IsNullOrEmpty(resourceName))
            {
                throw new FileNotFoundException("找不到嵌入的lz4c.exe资源");
            }

            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            {
                if (resourceStream == null)
                    throw new FileNotFoundException("无法读取嵌入的lz4c.exe资源");

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