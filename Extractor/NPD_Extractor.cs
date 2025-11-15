using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class NPD_Extractor : BaseExtractor
    {
        private static string _tempExePath;

        static NPD_Extractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "make_npdata.exe");
            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.make_npdata.exe"))
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
                OnExtractionFailed("错误:目录不存在");
                return;
            }
            var sdatFiles = Directory.GetFiles(directoryPath, "*.sdat", SearchOption.AllDirectories);
            if (sdatFiles.Length == 0)
            {
                OnExtractionFailed("未找到.sdat文件");
                return;
            }
            int validFileCount = 0;
            foreach (var file in sdatFiles)
            {
                if (CheckFileHeader(file))
                {
                    validFileCount++;
                }
            }
            if (validFileCount == 0)
            {
                OnExtractionFailed("未找到有效的NPD格式.sdat文件");
                return;
            }
            TotalFilesToExtract = validFileCount;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var sdatFilePath in sdatFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!CheckFileHeader(sdatFilePath))
                        {
                            continue;
                        }
                        string outputFilePath = Path.ChangeExtension(sdatFilePath, ".dec");
                        string? outputDir = Path.GetDirectoryName(outputFilePath);
                        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"-d \"{sdatFilePath}\" \"{outputFilePath}\" 0", 
                                WorkingDirectory = Path.GetDirectoryName(sdatFilePath),
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
                        if (process.ExitCode == 0 && File.Exists(outputFilePath))
                        {
                            OnFileExtracted(outputFilePath);
                        }
                        else
                        {
                            Console.WriteLine($"处理失败:{sdatFilePath}");
                            Console.WriteLine($"标准输出:{output}");
                            Console.WriteLine($"错误输出:{error}");
                        }
                    }
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"处理失败:{ex.Message}");
            }
        }
        private bool CheckFileHeader(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    byte[] header = br.ReadBytes(3);
                    return header.Length >= 3 &&
                           header[0] == 0x4E && // N
                           header[1] == 0x50 && // P
                           header[2] == 0x44;   // D
                }
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