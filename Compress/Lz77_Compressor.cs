using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Lz77_Compressor : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;
        private static string _tempRuntimeConfigPath;

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
                        throw new FileNotFoundException($"嵌入的LZ77资源未找到: {resourceName}");

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
                    foreach (var file in Directory.GetFiles(compressedDir, "*.lz77"))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToExtract = filesToCompress.Length;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileName(filePath);
                        string outputPath = Path.Combine(compressedDir, fileName + ".lz77");

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-c \"{filePath}\" -o \"{outputPath}\"",
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

                        Console.WriteLine($"Output: {output}");
                        Console.WriteLine($"Error: {error}");
                        Console.WriteLine($"Exit Code: {process.ExitCode}");

                        if (process.ExitCode == 0 && File.Exists(outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                        else
                        {
                            Console.WriteLine($"LZ77压缩失败: {filePath}");
                            Console.WriteLine($"输出文件是否存在: {File.Exists(outputPath)}");
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"LZ77压缩失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}