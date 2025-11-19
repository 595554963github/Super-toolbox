using ZE_CFSI_Lib;

namespace super_toolbox
{
    public class Cfsi_Repacker : BaseExtractor
    {
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await RepackAsync(directoryPath, cancellationToken);
        }

        public async Task RepackAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, $"目录{directoryPath}不存在");
                OnPackingFailed($"目录{directoryPath}不存在");
                return;
            }

            try
            {
                string parentDir = Directory.GetParent(directoryPath)?.FullName ?? directoryPath;
                string dirName = Path.GetFileName(directoryPath);
                string outputCfsiPath = Path.Combine(parentDir, dirName + ".cfsi");

                var sourceFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

                if (sourceFiles.Length == 0)
                {
                    PackingError?.Invoke(this, "目录中没有可打包的文件");
                    OnPackingFailed("目录中没有可打包的文件");
                    return;
                }

                TotalFilesToPack = sourceFiles.Length;
                PackingStarted?.Invoke(this, $"开始打包目录:{directoryPath} - 共{sourceFiles.Length}个文件");

                foreach (var sourceFile in sourceFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    OnFilePacked(sourceFile);
                    PackingProgress?.Invoke(this, $"正在打包:{Path.GetFileName(sourceFile)}");
                }

                PackingProgress?.Invoke(this, $"正在生成CFSI文件:{Path.GetFileName(outputCfsiPath)}");

                await Task.Run(() =>
                {
                    CFSI_Lib.Repack(directoryPath, outputCfsiPath);
                }, cancellationToken);

                if (File.Exists(outputCfsiPath))
                {
                    FileInfo fileInfo = new FileInfo(outputCfsiPath);
                    PackingProgress?.Invoke(this, $"打包完成:{Path.GetFileName(outputCfsiPath)} ({FormatFileSize(fileInfo.Length)})");
                    OnPackingCompleted();
                }
                else
                {
                    throw new FileNotFoundException("打包过程未生成输出文件", outputCfsiPath);
                }
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"打包时出错:{ex.Message}");
                OnPackingFailed($"打包时出错:{ex.Message}");
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        public void Repack(string directoryPath)
        {
            RepackAsync(directoryPath).Wait();
        }

        public override void Extract(string directoryPath)
        {
            Repack(directoryPath);
        }
    }
}