using System.Diagnostics;
using System.Reflection;

namespace super_toolbox
{
    public class TalesDat_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static string _tempExePath;
        static TalesDat_Extractor()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(tempDir);
            _tempExePath = Path.Combine(tempDir, "ToBTools.exe");
            if (!File.Exists(_tempExePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("embedded.ToBTools.exe"))
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
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                OnExtractionFailed("错误:目录路径为空");
                return;
            }
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误:目录不存在:{directoryPath}");
                return;
            }
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");
            try
            {
                var filesByDirectory = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".TLDAT", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".TOFHDB", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(Path.GetDirectoryName)
                    .Where(g => g.Count() >= 2);
                if (!filesByDirectory.Any())
                {
                    OnExtractionFailed("未找到配对的.TLDAT和.TOFHDB文件");
                    return;
                }
                int totalExtractedFiles = 0;
                await Task.Run(() =>
                {
                    foreach (var directoryGroup in filesByDirectory)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var tldatFiles = directoryGroup.Where(f => f.EndsWith(".TLDAT", StringComparison.OrdinalIgnoreCase)).ToList();
                        var tofhdbFiles = directoryGroup.Where(f => f.EndsWith(".TOFHDB", StringComparison.OrdinalIgnoreCase)).ToList();
                        int pairCount = Math.Min(tldatFiles.Count, tofhdbFiles.Count);
                        if (pairCount == 0) continue;
                        for (int i = 0; i < pairCount; i++)
                        {
                            try
                            {
                                string tldatFile = tldatFiles[i];
                                string tofhdbFile = tofhdbFiles[i];
                                ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(tldatFile)}和{Path.GetFileName(tofhdbFile)}");
                                using (var process = new Process())
                                {
                                    process.StartInfo = new ProcessStartInfo
                                    {
                                        FileName = _tempExePath,
                                        Arguments = $"unpack \"{tldatFile}\" \"{tofhdbFile}\"",
                                        WorkingDirectory = directoryGroup.Key,
                                        UseShellExecute = false,
                                        CreateNoWindow = true,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true
                                    };
                                    process.OutputDataReceived += (sender, e) =>
                                    {
                                        if (!string.IsNullOrEmpty(e.Data))
                                        {
                                            if (e.Data.Contains("/") && e.Data.Contains("."))
                                            {
                                                totalExtractedFiles++;
                                                OnFileExtracted(e.Data.Trim());
                                                ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(e.Data.Trim())}");
                                            }
                                            else
                                            {
                                                ExtractionProgress?.Invoke(this, e.Data);
                                            }
                                        }
                                    };
                                    process.ErrorDataReceived += (sender, e) =>
                                    {
                                        if (!string.IsNullOrEmpty(e.Data))
                                        {
                                            ExtractionError?.Invoke(this, $"错误:{e.Data}");
                                        }
                                    };
                                    process.Start();
                                    process.BeginOutputReadLine();
                                    process.BeginErrorReadLine();
                                    process.WaitForExit();
                                    ExtractionProgress?.Invoke(this, $"已完成:{Path.GetFileName(tldatFile)}");
                                }
                            }
                            catch (Exception ex)
                            {
                                ExtractionError?.Invoke(this, $"处理文件对时出错:{ex.Message}");
                            }
                        }
                    }
                    OnExtractionCompleted();
                    ExtractionProgress?.Invoke(this, $"提取完成，共提取{totalExtractedFiles}个文件");
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "操作已取消");
                OnExtractionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程中发生错误: {ex.Message}");
                OnExtractionFailed($"提取过程中发生错误: {ex.Message}");
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}