using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace super_toolbox
{
    public class lzhamCustom_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        private string? _decompressedDir;

        static lzhamCustom_Decompressor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "example1_x64.exe");
            ExtractEmbeddedResource("embedded.example1_x64.exe", _tempExePath);
        }

        private static void ExtractEmbeddedResource(string resourceName, string outputPath)
        {
            if (!File.Exists(outputPath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException($"嵌入的LZHAM资源未找到: {resourceName}");

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
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            _decompressedDir = Path.Combine(directoryPath, "Decompressed");
            Directory.CreateDirectory(_decompressedDir);

            foreach (var oldFile in Directory.GetFiles(_decompressedDir))
            {
                File.Delete(oldFile);
            }

            var allFiles = Directory.GetFiles(directoryPath, "*.*");
            var filesToProcess = allFiles.Where(IsLzhamFile).ToArray();

            if (filesToProcess.Length == 0)
            {
                OnExtractionFailed("未找到有效的LZHAM压缩文件");
                return;
            }

            TotalFilesToExtract = filesToProcess.Length;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(_decompressedDir, fileName);

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"d \"{filePath}\" \"{outputPath}\"",
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

                        Console.WriteLine($"处理文件: {filePath}");
                        Console.WriteLine($"输出: {output}");
                        Console.WriteLine($"错误: {error}");
                        Console.WriteLine($"退出代码: {process.ExitCode}");

                        if (process.ExitCode == 0 && File.Exists(outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                        else
                        {
                            Console.WriteLine($"LZHAM解压失败:{filePath}，未生成{outputPath}");
                        }
                    }

                    int finalCount = Directory.GetFiles(_decompressedDir).Length;
                    Console.WriteLine($"解压完成，Decompressed文件夹中共有{finalCount}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"LZHAM解压失败: {ex.Message}");
            }
        }

        private bool IsLzhamFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".lzham";
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}