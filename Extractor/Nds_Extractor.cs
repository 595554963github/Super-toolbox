using System.Diagnostics;

namespace super_toolbox
{
    public class Nds_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        private static string _tempExePath;
        static Nds_Extractor()
        {
            _tempExePath = LoadEmbeddedExe("embedded.ndstool.exe", "ndstool.exe");
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }
            var ndsFiles = Directory.GetFiles(directoryPath, "*.nds");
            if (ndsFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到NDS文件");
                OnExtractionFailed("未找到NDS文件");
                return;
            }
            TotalFilesToExtract = ndsFiles.Length;
            int successfullyExtractedCount = 0;
            int totalExtractedFiles = 0;
            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}，共找到{ndsFiles.Length}个NDS文件");
            try
            {
                foreach (var ndsFile in ndsFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    ExtractionProgress?.Invoke(this, $"正在解包NDS文件:{Path.GetFileName(ndsFile)}");

                    string fileName = Path.GetFileNameWithoutExtension(ndsFile);
                    string extractDir = Path.Combine(directoryPath, fileName);
                    Directory.CreateDirectory(extractDir);
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _tempExePath,
                            Arguments = $"-x \"{ndsFile}\" -9 \"{Path.Combine(extractDir, "arm9.bin")}\" -7 \"{Path.Combine(extractDir, "arm7.bin")}\" -y9 \"{Path.Combine(extractDir, "y9.bin")}\" -y7 \"{Path.Combine(extractDir, "y7.bin")}\" -d \"{Path.Combine(extractDir, "data")}\" -y \"{Path.Combine(extractDir, "overlay")}\" -t \"{Path.Combine(extractDir, "banner.bin")}\" -h \"{Path.Combine(extractDir, "header.bin")}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        ExtractionError?.Invoke(this, $"解包失败{Path.GetFileName(ndsFile)}:{error}");
                        continue;
                    }
                    var extractedFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
                    totalExtractedFiles += extractedFiles.Length;
                    foreach (var extractedFile in extractedFiles)
                    {
                        string extractedFileName = Path.GetFileName(extractedFile);
                        OnFileExtracted(extractedFile);
                        ExtractionProgress?.Invoke(this, $"已提取:{extractedFileName}");
                    }
                    successfullyExtractedCount++;
                    ExtractionProgress?.Invoke(this, $"成功解包:{Path.GetFileName(ndsFile)} -> {extractedFiles.Length}个文件");
                }
                if (successfullyExtractedCount > 0)
                {
                    ExtractionProgress?.Invoke(this, $"解包完成，成功解包{successfullyExtractedCount}个NDS文件，共提取{totalExtractedFiles}个文件");
                    OnExtractionCompleted();
                }
                else
                {
                    ExtractionError?.Invoke(this, "所有NDS文件解包都失败了");
                    OnExtractionFailed("所有NDS文件解包都失败了");
                }
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "解包操作已取消");
                OnExtractionFailed("解包操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解包过程出错: {ex.Message}");
                OnExtractionFailed($"解包过程出错: {ex.Message}");
            }
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}