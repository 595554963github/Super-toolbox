using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace super_toolbox
{
    public class PsarcExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static string _tempExePath;
        private int _extractionFailedCount = 0;
        private int _totalExtractedFiles = 0;

        static PsarcExtractor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);

            _tempExePath = Path.Combine(tempDir, "Unpsarc.exe");
            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.Unpsarc.exe"))
                {
                    if (stream == null)
                        throw new FileNotFoundException("嵌入的Unpsarc.exe资源未找到");

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
                string errorMsg = "错误:目录不存在";
                ExtractionError?.Invoke(this, errorMsg);
                base.OnExtractionFailed(errorMsg);
                _extractionFailedCount++;
                return;
            }

            var psarcFiles = Directory.GetFiles(directoryPath, "*.psarc");
            var pakFiles = Directory.GetFiles(directoryPath, "*.pak");
            var archiveFiles = new string[psarcFiles.Length + pakFiles.Length];
            psarcFiles.CopyTo(archiveFiles, 0);
            pakFiles.CopyTo(archiveFiles, psarcFiles.Length);

            if (archiveFiles.Length == 0)
            {
                string errorMsg = "未找到.psarc或.pak文件";
                ExtractionError?.Invoke(this, errorMsg);
                base.OnExtractionFailed(errorMsg);
                _extractionFailedCount++;
                return;
            }

            TotalFilesToExtract = archiveFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理目录: {directoryPath}，共找到{TotalFilesToExtract}个归档文件");

            int processedFiles = 0;
            _totalExtractedFiles = 0;

            try
            {
                foreach (var archiveFilePath in archiveFiles)
                {
                    base.ThrowIfCancellationRequested(cancellationToken);
                    processedFiles++;

                    string fileName = Path.GetFileName(archiveFilePath);
                    ExtractionProgress?.Invoke(this, $"正在解包文件:{fileName} ({processedFiles}/{TotalFilesToExtract})");

                    string archiveDir = Path.GetDirectoryName(archiveFilePath) ?? Directory.GetCurrentDirectory();
                    string archiveFileName = Path.GetFileNameWithoutExtension(archiveFilePath);

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"-x \"{archiveFileName}\"",
                            WorkingDirectory = archiveDir,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                            StandardErrorEncoding = Encoding.GetEncoding("GBK")
                        }
                    };

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            ExtractionProgress?.Invoke(this, $"[{fileName}] {e.Data}");
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            ExtractionError?.Invoke(this, $"[{fileName}]错误:{e.Data}");
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode != 0)
                    {
                        string errorMsg = $"解包失败:{fileName}-退出码:{process.ExitCode}";
                        ExtractionError?.Invoke(this, errorMsg);
                        base.OnExtractionFailed(errorMsg);
                        _extractionFailedCount++;
                        continue;
                    }

                    string expectedOutputDir = Path.Combine(archiveDir, archiveFileName);
                    if (Directory.Exists(expectedOutputDir))
                    {
                        var extractedFiles = Directory.GetFiles(expectedOutputDir, "*", SearchOption.AllDirectories);
                        int fileCount = extractedFiles.Length;
                        _totalExtractedFiles += fileCount;

                        foreach (var extractedFile in extractedFiles)
                        {
                            var relativePath = Path.GetRelativePath(expectedOutputDir, extractedFile);
                            string fullRelativePath = Path.Combine(archiveFileName, relativePath);
                            OnFileExtracted(fullRelativePath);
                        }

                        ExtractionProgress?.Invoke(this, $"解包成功: {fileName} -> 提取出{fileCount}个文件");
                    }
                    else
                    {
                        string errorMsg = $"解包失败:{fileName}-未找到输出目录 {Path.GetFileName(expectedOutputDir)}";
                        ExtractionError?.Invoke(this, errorMsg);
                        base.OnExtractionFailed(errorMsg);
                        _extractionFailedCount++;
                    }
                }

                int successCount = TotalFilesToExtract - _extractionFailedCount;
                ExtractionProgress?.Invoke(this, $"处理完成！共处理{TotalFilesToExtract}个归档文件，成功解包{successCount}个，失败{_extractionFailedCount}个，共提取出{_totalExtractedFiles}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                string cancelMsg = "操作已取消";
                ExtractionError?.Invoke(this, cancelMsg);
                base.OnExtractionFailed(cancelMsg);
                _extractionFailedCount++;
            }
            catch (Exception ex)
            {
                string errorMsg = $"处理失败:{ex.Message}";
                ExtractionError?.Invoke(this, errorMsg);
                base.OnExtractionFailed(errorMsg);
                _extractionFailedCount++;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}