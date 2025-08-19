using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace super_toolbox
{
    public class Nds_Repacker : BaseExtractor
    {
        private static string _tempExePath;

        static Nds_Repacker()
        {
            _tempExePath = LoadEmbeddedExe("embedded.ndstool.exe", "ndstool.exe");

            if (string.IsNullOrEmpty(_tempExePath) || !File.Exists(_tempExePath))
            {
                throw new InvalidOperationException("无法加载ndstool.exe，请检查嵌入资源");
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            if (CheckRequiredFiles(directoryPath))
            {
                TotalFilesToExtract = 1;
                await ProcessDirectory(directoryPath, directoryPath, cancellationToken);
                OnExtractionCompleted();
                return;
            }

            var validDirs = Directory.GetDirectories(directoryPath)
                .Where(dir => CheckRequiredFiles(dir))
                .ToList();

            if (validDirs.Count == 0)
            {
                OnExtractionFailed("未找到可打包的NDS文件结构");
                return;
            }

            TotalFilesToExtract = validDirs.Count;

            try
            {
                foreach (var dir in validDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ProcessDirectory(dir, directoryPath, cancellationToken);
                }

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("打包操作已取消");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"打包失败: {ex.Message}");
            }
        }

        private async Task ProcessDirectory(string sourceDir, string outputDir, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string ndsFileName = Path.GetFileName(sourceDir) + ".nds";
            string outputPath = Path.Combine(outputDir, ndsFileName);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            string arm9Path = Path.Combine(sourceDir, "arm9.bin");
            string arm7Path = Path.Combine(sourceDir, "arm7.bin");
            string y9Path = Path.Combine(sourceDir, "y9.bin");
            string y7Path = Path.Combine(sourceDir, "y7.bin");
            string dataPath = Path.Combine(sourceDir, "data");
            string overlayPath = Path.Combine(sourceDir, "overlay");
            string bannerPath = Path.Combine(sourceDir, "banner.bin");
            string headerPath = Path.Combine(sourceDir, "header.bin");

            string arguments = $"-c \"{outputPath}\" " +
                              $"-9 \"{arm9Path}\" " +
                              $"-7 \"{arm7Path}\" " +
                              $"-y9 \"{y9Path}\" " +
                              $"-y7 \"{y7Path}\" " +
                              $"-d \"{dataPath}\" " +
                              $"-y \"{overlayPath}\" " +
                              $"-t \"{bannerPath}\" " +
                              $"-h \"{headerPath}\"";

            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _tempExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(_tempExePath) ?? sourceDir
                }
            })
            {
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(cancellationToken);

                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode != 0)
                {
                    throw new Exception($"打包失败 (ExitCode: {process.ExitCode})。输出: {output}。错误: {error}");
                }
            }

            if (File.Exists(outputPath))
            {
                OnFileExtracted(outputPath);
            }
            else
            {
                throw new FileNotFoundException("打包过程未生成输出文件", outputPath);
            }
        }

        private bool CheckRequiredFiles(string directory)
        {
            bool hasArm9 = File.Exists(Path.Combine(directory, "arm9.bin"));
            bool hasArm7 = File.Exists(Path.Combine(directory, "arm7.bin"));
            bool hasY9 = File.Exists(Path.Combine(directory, "y9.bin"));
            bool hasY7 = File.Exists(Path.Combine(directory, "y7.bin"));
            bool hasDataDir = Directory.Exists(Path.Combine(directory, "data"));
            bool hasOverlayDir = Directory.Exists(Path.Combine(directory, "overlay"));
            bool hasBanner = File.Exists(Path.Combine(directory, "banner.bin"));
            bool hasHeader = File.Exists(Path.Combine(directory, "header.bin"));

            if (!hasArm9) Debug.WriteLine($"缺少文件: {Path.Combine(directory, "arm9.bin")}");
            if (!hasArm7) Debug.WriteLine($"缺少文件: {Path.Combine(directory, "arm7.bin")}");

            return hasArm9 && hasArm7 && hasY9 && hasY7 &&
                   hasDataDir && hasOverlayDir && hasBanner && hasHeader;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
