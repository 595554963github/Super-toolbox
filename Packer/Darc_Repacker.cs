using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace super_toolbox
{
    public class Darc_Repacker : BaseExtractor
    {
        private static string _tempExePath;

        static Darc_Repacker()
        {
            _tempExePath = LoadEmbeddedExe("embedded.darctool.exe", "darctool.exe");

            if (string.IsNullOrEmpty(_tempExePath) || !File.Exists(_tempExePath))
            {
                throw new InvalidOperationException("无法加载darctool.exe，请检查嵌入资源");
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed("错误：目录不存在");
                return;
            }

            if (Directory.GetFiles(directoryPath).Length > 0 || Directory.GetDirectories(directoryPath).Length > 0)
            {
                TotalFilesToExtract = 1;
                await ProcessDirectory(directoryPath, directoryPath, cancellationToken);
                OnExtractionCompleted();
                return;
            }

            var validDirs = Directory.GetDirectories(directoryPath)
                .Where(dir => Directory.GetFiles(dir).Length > 0 || Directory.GetDirectories(dir).Length > 0)
                .ToList();

            if (validDirs.Count == 0)
            {
                OnExtractionFailed("未找到可打包的DARC文件结构");
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

            string darcFileName = Path.GetFileName(sourceDir) + ".darc";
            string outputPath = Path.Combine(outputDir, darcFileName);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            string arguments = $"-cvfd \"{outputPath}\" \"{sourceDir}\"";

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

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}