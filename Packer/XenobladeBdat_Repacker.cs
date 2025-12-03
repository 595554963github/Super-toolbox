using super_toolbox;
using System.Diagnostics;
using System.Text;

public class XenobladeBdat_Repacker : BaseExtractor
{
    private static string _tempExePath;

    public new event EventHandler<string>? PackingStarted;
    public new event EventHandler<string>? PackingProgress;
    public new event EventHandler<string>? PackingError;

    static XenobladeBdat_Repacker()
    {
        _tempExePath = LoadEmbeddedExe("embedded.bdat-toolset.exe", "bdat-toolset.exe");
    }

    public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            PackingError?.Invoke(this, $"错误:目录{directoryPath}不存在");
            OnPackingFailed($"错误:目录{directoryPath}不存在");
            return;
        }

        var validPackages = new List<(string jsonDir, string outputName)>();
        foreach (var item in Directory.GetDirectories(directoryPath))
        {
            string dirName = Path.GetFileName(item);
            var jsonFiles = Directory.GetFiles(item, "*.json", SearchOption.AllDirectories);
            if (jsonFiles.Length > 0)
            {
                validPackages.Add((item, dirName));
                PackingProgress?.Invoke(this, $"找到可打包目录:{dirName} (JSON:{jsonFiles.Length}个)");
            }
        }

        if (validPackages.Count == 0)
        {
            PackingError?.Invoke(this, "未找到包含JSON文件的目录");
            OnPackingFailed("未找到包含JSON文件的目录");
            return;
        }

        TotalFilesToPack = validPackages.Count;
        PackingStarted?.Invoke(this, $"开始打包{validPackages.Count}个目录到:{directoryPath}");

        try
        {
            await Task.Run(() =>
            {
                foreach (var package in validPackages)
                {
                    ThrowIfCancellationRequested(cancellationToken);
                    string jsonDir = package.jsonDir;
                    string outputName = package.outputName;
                    PackingProgress?.Invoke(this, $"正在打包:{outputName}");

                    try
                    {
                        string outputFile = Path.Combine(directoryPath, $"{outputName}.bdat");
                        if (File.Exists(outputFile))
                        {
                            try
                            {
                                File.Delete(outputFile);
                                PackingProgress?.Invoke(this, $"已删除已存在的文件:{Path.GetFileName(outputFile)}");
                            }
                            catch (Exception ex)
                            {
                                PackingError?.Invoke(this, $"无法删除已存在文件:{ex.Message}");
                                continue;
                            }
                        }

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = _tempExePath,
                                Arguments = $"pack \"{jsonDir}\" -o \"{directoryPath}\" -f json",
                                WorkingDirectory = Path.GetDirectoryName(_tempExePath) ?? directoryPath,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                StandardOutputEncoding = Encoding.UTF8,
                                StandardErrorEncoding = Encoding.UTF8
                            }
                        };

                        process.Start();

                        process.WaitForExit();

                        string expectedFile = Path.Combine(directoryPath, $"{outputName}.bdat");
                        if (process.ExitCode == 0 && File.Exists(expectedFile))
                        {
                            FileInfo fileInfo = new FileInfo(expectedFile);
                            OnFilePacked(expectedFile);
                            PackingProgress?.Invoke(this, $"成功打包:{outputName} ({FormatFileSize(fileInfo.Length)})");
                        }
                        else if (process.ExitCode != 0)
                        {
                            PackingError?.Invoke(this, $"打包失败,退出代码:{process.ExitCode}");
                            OnPackingFailed($"打包{outputName}失败,退出代码:{process.ExitCode}");
                        }
                        else
                        {
                            PackingError?.Invoke(this, $"打包成功但未生成文件:{outputName}.bdat");
                            OnPackingFailed($"打包成功但未生成文件:{outputName}.bdat");
                        }
                    }
                    catch (Exception ex)
                    {
                        PackingError?.Invoke(this, $"{outputName}打包错误:{ex.Message}");
                        OnPackingFailed($"{outputName}打包错误:{ex.Message}");
                    }
                }

                OnPackingCompleted();
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            PackingError?.Invoke(this, "打包操作已取消");
            OnPackingFailed("打包操作已取消");
        }
        catch (Exception ex)
        {
            PackingError?.Invoke(this, $"打包失败:{ex.Message}");
            OnPackingFailed($"打包失败:{ex.Message}");
        }
    }
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
    public override void Extract(string directoryPath)
    {
        ExtractAsync(directoryPath).Wait();
    }
}