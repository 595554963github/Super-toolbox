using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Lz77_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        private static string _tempDllPath;
        private static string _tempRuntimeConfigPath;

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

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    if (allFiles.Length == 0)
                    {
                        OnExtractionFailed("未找到LZ77压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);

                    TotalFilesToExtract = allFiles.Length;

                    foreach (var filePath in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(decompressedDir, fileName);

                        if (DecompressLz77File(filePath, outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"LZ77解压失败: {ex.Message}");
            }
        }

        private bool DecompressLz77File(string inputPath, string outputPath)
        {
            try
            {
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

                Console.WriteLine($"Output: {output}");
                Console.WriteLine($"Error: {error}");
                Console.WriteLine($"Exit Code: {process.ExitCode}");

                return process.ExitCode == 0 && File.Exists(outputPath);
            }
            catch
            {
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}