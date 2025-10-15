using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class DtsExtractor : BaseExtractor
    {
        private static string _tempExePath;

        static DtsExtractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "SRPG_Unpacker.exe");

            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.SRPG_Unpacker.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的EXE资源未找到");

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

            var dtsFiles = Directory.GetFiles(directoryPath, "*.dts");
            if (dtsFiles.Length == 0)
            {
                OnExtractionFailed("未找到.dts文件");
                return;
            }

            TotalFilesToExtract = dtsFiles.Length;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var dtsFilePath in dtsFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"\"{dtsFilePath}\"",
                                WorkingDirectory = Path.GetDirectoryName(dtsFilePath),
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };

                        process.Start();
                        process.WaitForExit();

                        OnFileExtracted(dtsFilePath);
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"处理失败: {ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}