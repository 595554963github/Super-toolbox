using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace super_toolbox
{
    public class Lzss_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        private static readonly byte[] LZSSMagic = { 0xA0, 0x55, 0x6A, 0x94, 0x68, 0x04 };

        static Lzss_Decompressor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "sample.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.sample.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的LZSS解压工具资源未找到");

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
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
                    var filesToProcess = allFiles.Where(IsLZSSFile).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        OnExtractionFailed("未找到包含LZSS数据的文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);

                    TotalFilesToExtract = filesToProcess.Length;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(decompressedDir, fileName + ".bin"); 

                        ProcessLZSSFile(filePath, outputPath);

                        if (File.Exists(outputPath))
                        {
                            OnFileExtracted(outputPath);
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"解压失败: {ex.Message}");
            }
        }

        private bool IsLZSSFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[6];
                    int bytesRead = fs.Read(buffer, 0, 6);

                    return bytesRead == 6 && buffer.SequenceEqual(LZSSMagic);
                }
            }
            catch { }
            return false;
        }

        private void ProcessLZSSFile(string inputPath, string outputPath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"-d -i \"{inputPath}\" -o \"{outputPath}\"",
                        WorkingDirectory = Path.GetDirectoryName(inputPath),
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
                    throw new Exception($"解压失败: {error}");
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                throw new Exception($"处理文件 {Path.GetFileName(inputPath)} 时出错: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}