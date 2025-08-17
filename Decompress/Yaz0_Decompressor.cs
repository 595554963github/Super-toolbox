using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace super_toolbox
{
    public class Yaz0_Decompressor : BaseExtractor
    {
        private static string _tempExePath;
        private static readonly byte[] Yaz0Magic = { 0x59, 0x61, 0x7A, 0x30 }; 

        static Yaz0_Decompressor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "yaz0.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.yaz0.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的Yaz0解压工具资源未找到");

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
                    var allFiles = Directory.GetFiles(directoryPath, "*.*");
                    var filesToProcess = allFiles.Where(IsYaz0File).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        OnExtractionFailed("未找到包含Yaz0数据的文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);

                    TotalFilesToExtract = filesToProcess.Length;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(decompressedDir, fileName + ".decompress");

                        ProcessYaz0File(filePath, outputPath);

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

        private bool IsYaz0File(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[50];
                    int bytesRead = fs.Read(buffer, 0, 50);

                    // Check if magic appears in first 50 bytes
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

        private void ProcessYaz0File(string inputPath, string outputPath)
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                int yaz0Start = FindYaz0StartPosition(inputPath);
                if (yaz0Start < 0) return;

                if (yaz0Start > 0)
                {
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

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _tempExePath,
                        Arguments = $"-d \"{tempFilePath}\" -o \"{outputPath}\"",
                        WorkingDirectory = Path.GetDirectoryName(inputPath),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();

                if (yaz0Start > 0 && File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
                throw;
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