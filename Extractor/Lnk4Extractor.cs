using System.Diagnostics;

namespace super_toolbox
{
    public class LNK4Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static string _tempExePath;

        static LNK4Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.exlnk4.exe", "exlnk4.exe");
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, "错误:目录不存在或路径为空");
                OnExtractionFailed("错误:目录不存在或路径为空");
                return;
            }

            var datFiles = Directory.GetFiles(directoryPath, "*.dat", SearchOption.AllDirectories);

            if (datFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.dat文件");
                OnExtractionFailed("未找到.dat文件");
                return;
            }

            TotalFilesToExtract = datFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{datFiles.Length}个文件(.dat)");

            try
            {
                await Task.Run(() =>
                {
                    int processedCount = 0;
                    int totalExtractedFiles = 0;

                    foreach (var filePath in datFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        processedCount++;
                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)} ({processedCount}/{datFiles.Length})");

                        try
                        {
                            string? parentDir = Path.GetDirectoryName(filePath);
                            if (string.IsNullOrEmpty(parentDir)) continue;

                            var beforeSnapshot = GetDirectorySnapshotRecursive(parentDir);

                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = _tempExePath,
                                    Arguments = $"\"{filePath}\"",
                                    WorkingDirectory = parentDir,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                }
                            };

                            process.Start();

                            var extractedInThisFile = 0;
                            while (!process.HasExited)
                            {
                                Thread.Sleep(50);
                                var currentSnapshot = GetDirectorySnapshotRecursive(parentDir);
                                var newFiles = currentSnapshot.Except(beforeSnapshot).ToList();

                                foreach (var newFile in newFiles)
                                {
                                    beforeSnapshot.Add(newFile);
                                    extractedInThisFile++;
                                    totalExtractedFiles++;
                                    OnFileExtracted(Path.Combine(parentDir, newFile));
                                    ExtractionProgress?.Invoke(this, $"已提取:{newFile}");
                                }
                            }

                            if (extractedInThisFile > 0)
                            {
                                ExtractionProgress?.Invoke(this, $"完成处理:{Path.GetFileName(filePath)} -> {extractedInThisFile}个文件");
                            }
                            else if (process.ExitCode != 0)
                            {
                                ExtractionError?.Invoke(this, $"处理失败:{Path.GetFileName(filePath)}，退出代码:{process.ExitCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"处理错误:{Path.GetFileName(filePath)} - {ex.Message}");
                        }
                    }

                    ExtractionProgress?.Invoke(this, $"提取完成，总共提取了{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取失败:{ex.Message}");
                OnExtractionFailed($"提取失败:{ex.Message}");
            }
        }

        private HashSet<string> GetDirectorySnapshotRecursive(string directory)
        {
            var snapshot = new HashSet<string>();

            try
            {
                var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(directory.Length + 1).Replace('\\', '/'));

                foreach (var file in files)
                {
                    snapshot.Add(file);
                }
            }
            catch { }

            return snapshot;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
