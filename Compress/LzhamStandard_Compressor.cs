using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class LzhamStandard_Compressor : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;
        private string? _compressedDir;

        static LzhamStandard_Compressor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "lzhamtest_x64.exe");
            _tempDllPath = Path.Combine(tempDir, "lzham_x64.dll");

            ExtractEmbeddedResource("embedded.lzhamtest_x64.exe", _tempExePath);
            ExtractEmbeddedResource("embedded.lzham_x64.dll", _tempDllPath);
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

            _compressedDir = Path.Combine(directoryPath, "Compressed");
            Directory.CreateDirectory(_compressedDir);

            var filesToProcess = Directory.GetFiles(directoryPath, "*.*");
            if (filesToProcess.Length == 0)
            {
                OnExtractionFailed("未找到需要处理的文件");
                return;
            }

            foreach (var oldFile in Directory.GetFiles(_compressedDir, "*.lzham"))
            {
                File.Delete(oldFile);
            }

            TotalFilesToExtract = filesToProcess.Length;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(filePath);
                        string outputPath = Path.Combine(_compressedDir, $"{fileName}.lzham");

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"c \"{filePath}\" \"{outputPath}\"",
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
                            Console.WriteLine($"LZHAM处理失败:{filePath}，未生成 {outputPath}");
                        }
                    }

                    int finalCount = Directory.GetFiles(_compressedDir, "*.lzham").Length;
                    Console.WriteLine($"压缩完成，Compressed文件夹中共有{finalCount}个.lzham文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"LZHAM处理失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}