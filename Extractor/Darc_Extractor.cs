using System.Diagnostics;
using System.Text;

namespace super_toolbox
{
    public class Darc_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static string _tempExePath;
        static Darc_Extractor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _tempExePath = LoadEmbeddedExe("embedded.darctool.exe", "darctool.exe");
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误:目录{directoryPath}不存在");
                return;
            }
            var darcFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(IsDarcFile)
                .ToList();

            if (darcFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, "未找到DARC格式文件");
                OnExtractionFailed("未找到DARC格式文件");
                return;
            }
            ExtractionStarted?.Invoke(this, $"开始处理{darcFiles.Count}个DARC文件");

            try
            {
                int totalExtractedFiles = 0;

                foreach (var darcFile in darcFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(darcFile)}");
                    string fileDirectory = Path.GetDirectoryName(darcFile) ?? directoryPath;
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(darcFile);
                    string outputDir = Path.Combine(fileDirectory, fileNameWithoutExtension);
                    if (Directory.Exists(outputDir))
                    {
                        Directory.Delete(outputDir, true);
                    }
                    Directory.CreateDirectory(outputDir);
                    var extractedFiles = await ExtractDarcFile(darcFile, outputDir, cancellationToken);
                    totalExtractedFiles += extractedFiles.Count;
                    foreach (var file in extractedFiles)
                    {
                        OnFileExtracted(file);
                    }
                    ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(darcFile)} -> {extractedFiles.Count}个文件");
                }
                TotalFilesToExtract = totalExtractedFiles;
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "解包操作已取消");
                OnExtractionFailed("解包操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包失败:{ex.Message}");
                OnExtractionFailed($"解包失败:{ex.Message}");
            }
        }
        private async Task<List<string>> ExtractDarcFile(string inputFile, string outputDir, CancellationToken cancellationToken)
        {
            var extractedFiles = new List<string>();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = $"-xvfd \"{inputFile}\" \"{outputDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                    StandardErrorEncoding = Encoding.GetEncoding("GBK")
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();
                await process.WaitForExitAsync(cancellationToken);
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        ExtractionProgress?.Invoke(this, line);
                    }
                }
                if (!string.IsNullOrEmpty(errorOutput))
                {
                    foreach (string line in errorOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        ExtractionError?.Invoke(this, $"错误:{line}");
                    }
                }
                if (process.ExitCode != 0)
                {
                    throw new Exception($"解包失败(ExitCode:{process.ExitCode}):{errorOutput}");
                }
                if (Directory.Exists(outputDir))
                {
                    var files = Directory.GetFiles(outputDir, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string relativePath = GetRelativePath(outputDir, file);
                        extractedFiles.Add(relativePath);
                        ExtractionProgress?.Invoke(this, $"已提取:{relativePath}");
                    }
                }
            }
            return extractedFiles;
        }
        private string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;
            Uri baseUri = new Uri(basePath);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
        private bool IsDarcFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 4) return false;
                    byte[] header = new byte[4];
                    fs.Read(header, 0, 4);
                    return header[0] == 0x64 &&
                           header[1] == 0x61 &&
                           header[2] == 0x72 &&
                           header[3] == 0x63;
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