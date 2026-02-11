using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;

namespace super_toolbox
{
    public class JavaDecompiler : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await DecompileAsync(new DecompilerConfig { InputPath = directoryPath, OutputPath = Path.Combine(directoryPath, "decompiled") }, cancellationToken);
        }

        public async Task DecompileAsync(DecompilerConfig config, CancellationToken cancellationToken = default)
        {
            ConversionStarted?.Invoke(this, $"开始反编译:{config.InputPath}");

            try
            {
                var taskManager = new TaskManager(config.ThreadCount);
                var jobs = await PrepareDecompileJobsAsync(config, taskManager);

                if (jobs.Count == 0)
                {
                    ConversionProgress?.Invoke(this, "没有找到可反编译的文件");
                    OnConversionCompleted();
                    return;
                }

                TotalFilesToConvert = jobs.Count;
                ConversionProgress?.Invoke(this, $"共找到{jobs.Count}个待转换的class文件");

                int successCount = 0;
                int currentJob = 0;

                foreach (var job in jobs)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    currentJob++;
                    ConversionProgress?.Invoke(this, $"[{currentJob}/{jobs.Count}] 正在处理:{job.SourceFile.Name}");

                    var result = await new DecompileTask(job, config, BuildDefaultOptions()).ExecuteAsync();

                    if (result.Success)
                    {
                        successCount++;
                        ConversionProgress?.Invoke(this, $"[{currentJob}/{jobs.Count}] 转换成功:{Path.GetFileName(job.SourceFile.Name)} -> {Path.GetFileName(result.OutputPath)}");
                        OnFileConverted(result.OutputPath ?? string.Empty);
                    }
                    else
                    {
                        ConversionError?.Invoke(this, $"[{currentJob}/{jobs.Count}] {job.SourceFile.Name}转换失败:{result.ErrorMessage}");
                        OnConversionFailed($"{job.SourceFile.Name}转换失败:{result.ErrorMessage}");
                    }

                    int percentage = (currentJob * 100) / jobs.Count;
                    ConversionProgress?.Invoke(this, $"进度:{percentage}% ({currentJob}/{jobs.Count})");
                }

                ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个class文件,生成{successCount}个java文件");
                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }

        private Dictionary<string, object> BuildDefaultOptions()
        {
            return new OptionsBuilder()
                .WithGenerics(true)
                .WithInnerClasses(true)
                .WithEnums(true)
                .WithLocalVarNames(true)
                .WithMethodParams(true)
                .WithIndent("    ")
                .Build();
        }

        private async Task<List<DecompileJob>> PrepareDecompileJobsAsync(DecompilerConfig config, TaskManager taskManager)
        {
            var inputFile = new FileInfo(config.InputPath);
            var jobs = new List<DecompileJob>();

            if (inputFile.Attributes.HasFlag(FileAttributes.Directory))
            {
                jobs = await FileUtil.ProcessDirectoryAsync(new DirectoryInfo(config.InputPath), config.OutputPath, taskManager);
            }
            else if (JarUtil.IsJarFile(inputFile))
            {
                jobs = await FileUtil.ProcessJarFileAsync(inputFile, config.OutputPath);
            }
            else if (inputFile.Name.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
            {
                jobs = await FileUtil.ProcessClassFileAsync(inputFile, config.OutputPath);
            }
            else
            {
                throw new ArgumentException("输入必须是class文件、jar文件或目录");
            }

            return jobs;
        }
    }

    public class DecompilerConfig
    {
        public string InputPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public int ThreadCount { get; set; } = Environment.ProcessorCount;
        public bool DeleteClassFiles { get; set; } = false;
    }

    public class OptionsBuilder
    {
        private readonly Dictionary<string, object> _options;

        public OptionsBuilder()
        {
            _options = VineflowerOptions.LoadDefaultOptions();
        }

        public OptionsBuilder WithOption(string key, string value)
        {
            _options[key] = value;
            return this;
        }

        public OptionsBuilder WithGenerics(bool enable)
        {
            _options[VineflowerOptions.DGS] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithInnerClasses(bool enable)
        {
            _options[VineflowerOptions.DIN] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithAssertions(bool enable)
        {
            _options[VineflowerOptions.DAS] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithEnums(bool enable)
        {
            _options[VineflowerOptions.DEN] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithPreviewFeatures(bool enable)
        {
            _options[VineflowerOptions.DPR] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithSwitchExpressions(bool enable)
        {
            _options[VineflowerOptions.SWE] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithPatternMatching(bool enable)
        {
            _options[VineflowerOptions.PAM] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithLocalVarNames(bool enable)
        {
            _options[VineflowerOptions.UDV] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithMethodParams(bool enable)
        {
            _options[VineflowerOptions.UMP] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithRemoveEmptyTryCatch(bool enable)
        {
            _options[VineflowerOptions.RER] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithHideEmptySuper(bool enable)
        {
            _options[VineflowerOptions.HES] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithHideDefaultConstructor(bool enable)
        {
            _options[VineflowerOptions.HDC] = enable ? "1" : "0";
            return this;
        }

        public OptionsBuilder WithThreads(int threadCount)
        {
            if (threadCount > 0)
                _options[VineflowerOptions.THR] = threadCount.ToString();
            return this;
        }

        public OptionsBuilder WithIndent(string indentString)
        {
            _options[VineflowerOptions.IND] = indentString;
            return this;
        }

        public Dictionary<string, object> Build()
        {
            return new Dictionary<string, object>(_options);
        }
    }

    public static class VineflowerOptions
    {
        public const string DIN = "din";
        public const string DGS = "dgs";
        public const string DAS = "das";
        public const string DEN = "den";
        public const string DPR = "dpr";
        public const string SWE = "swe";
        public const string PAM = "pam";
        public const string UDV = "udv";
        public const string UMP = "ump";
        public const string RER = "rer";
        public const string HES = "hes";
        public const string HDC = "hdc";
        public const string THR = "thr";
        public const string IND = "ind";

        public static Dictionary<string, object> LoadDefaultOptions()
        {
            var options = new Dictionary<string, object>
            {
                { DIN, "1" },
                { DGS, "1" },
                { DAS, "1" },
                { DEN, "1" },
                { DPR, "1" },
                { SWE, "1" },
                { PAM, "1" },
                { UDV, "1" },
                { UMP, "1" },
                { RER, "1" },
                { HES, "1" },
                { HDC, "1" },
                { THR, Environment.ProcessorCount.ToString() },
                { IND, "   " }
            };
            return options;
        }
    }

    public class DecompileJob
    {
        public FileInfo SourceFile { get; set; }
        public string TargetPath { get; set; }
        public string? RelativePath { get; set; }

        public DecompileJob(FileInfo sourceFile, string targetPath, string? relativePath)
        {
            SourceFile = sourceFile;
            TargetPath = targetPath;
            RelativePath = relativePath;
        }
    }

    public class DecompileResult
    {
        public DecompileJob Job { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? OutputPath { get; set; }

        public DecompileResult(DecompileJob job, bool success, string? errorMessage, string? outputPath)
        {
            Job = job;
            Success = success;
            ErrorMessage = errorMessage;
            OutputPath = outputPath;
        }
    }

    public class TaskManager
    {
        private readonly SemaphoreSlim _semaphore;

        public TaskManager(int threadCount)
        {
            _semaphore = new SemaphoreSlim(threadCount, threadCount);
        }

        public async Task<List<DecompileResult>> ProcessTasksAsync<T>(
            List<T> items,
            Func<T, Task<DecompileResult>> taskFactory,
            Action<DecompileResult>? resultProcessor)
        {
            var results = new List<DecompileResult>();
            var tasks = new List<Task>();
            var totalTasks = items.Count;
            var completedTasks = 0;
            var lastReportedPercentage = 0;
            var lockObj = new object();

            foreach (var item in items)
            {
                await _semaphore.WaitAsync();
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var result = await taskFactory(item);
                        lock (lockObj)
                        {
                            results.Add(result);
                            resultProcessor?.Invoke(result);
                            completedTasks++;
                            var percentage = completedTasks * 100 / totalTasks;
                            if (percentage >= lastReportedPercentage + 10 || completedTasks == totalTasks)
                            {
                                lastReportedPercentage = percentage;
                            }
                        }
                        return result;
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return results;
        }

        public async Task ExecuteParallelAsync(List<Func<Task>> tasks)
        {
            if (tasks.Count == 0) return;

            var parallelTasks = new List<Task>();
            foreach (var task in tasks)
            {
                await _semaphore.WaitAsync();
                parallelTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await task();
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }));
            }
            await Task.WhenAll(parallelTasks);
        }
    }

    public class DecompileTask
    {
        private readonly DecompileJob _job;
        private readonly DecompilerConfig _config;
        private readonly Dictionary<string, object> _options;

        public DecompileTask(DecompileJob job, DecompilerConfig config, Dictionary<string, object> options)
        {
            _job = job;
            _config = config;
            _options = options;
        }

        public async Task<DecompileResult> ExecuteAsync()
        {
            try
            {
                var classFile = _job.SourceFile;
                var outputDirPath = _job.TargetPath;
                var outputDir = new DirectoryInfo(outputDirPath);

                if (!outputDir.Exists)
                    outputDir.Create();

                if (!classFile.Exists || !IsFileReadable(classFile))
                    return new DecompileResult(_job, false, "源文件不存在或无法读取", null);

                var className = classFile.Name;
                var baseName = className.EndsWith(".class", StringComparison.OrdinalIgnoreCase)
                    ? className.Substring(0, className.Length - 6)
                    : className;

                var baseNameWithoutNumber = baseName;
                if (baseName.Contains("_"))
                {
                    var lastUnderscoreIndex = baseName.LastIndexOf("_");
                    if (lastUnderscoreIndex > 0 && lastUnderscoreIndex < baseName.Length - 1)
                    {
                        var possibleNumber = baseName.Substring(lastUnderscoreIndex + 1);
                        if (Regex.IsMatch(possibleNumber, "^\\d+$"))
                            baseNameWithoutNumber = baseName.Substring(0, lastUnderscoreIndex);
                    }
                }

                var result = await VineflowerInvoker.DecompileAsync(classFile.FullName, outputDir.FullName, _options);

                if (!result.Success)
                    return new DecompileResult(_job, false, result.ErrorMessage, null);

                var javaFiles = FindJavaFiles(outputDir, baseName, baseNameWithoutNumber);

                if (javaFiles.Length > 0)
                {
                    if (_config.DeleteClassFiles)
                    {
                        classFile.Delete();
                    }
                    return new DecompileResult(_job, true, null, javaFiles[0].FullName);
                }

                return new DecompileResult(_job, false, "未生成java文件", null);
            }
            catch (Exception ex)
            {
                return new DecompileResult(_job, false, ex.Message, null);
            }
        }

        private bool IsFileReadable(FileInfo file)
        {
            try
            {
                using (file.OpenRead()) { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private FileInfo[] FindJavaFiles(DirectoryInfo directory, string baseName, string baseNameWithoutNumber)
        {
            try
            {
                return directory.GetFiles("*.java", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var javaBaseName = Path.GetFileNameWithoutExtension(f.Name);
                        return javaBaseName.Equals(baseName) ||
                               javaBaseName.Equals(baseNameWithoutNumber) ||
                               javaBaseName.Equals(baseName.Replace('$', '_')) ||
                               javaBaseName.StartsWith(baseName + "$") ||
                               javaBaseName.StartsWith(baseNameWithoutNumber + "$");
                    })
                    .ToArray();
            }
            catch
            {
                return new FileInfo[0];
            }
        }
    }

    public static class FileUtil
    {
        public static async Task<List<DecompileJob>> ProcessDirectoryAsync(DirectoryInfo directory, string outputPath, TaskManager taskManager)
        {
            if (!directory.Exists)
                throw new FileNotFoundException($"目录不存在:{directory.FullName}");

            var outputDir = new DirectoryInfo(outputPath);
            if (!outputDir.Exists)
                outputDir.Create();

            var jarFiles = new List<FileInfo>();
            var classFiles = new List<FileInfo>();

            var allFiles = directory.GetFiles("*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                var fileName = file.Name.ToLower();
                if (fileName.EndsWith(".jar") || fileName.EndsWith(".war") ||
                    fileName.EndsWith(".ear") || fileName.EndsWith(".aar"))
                {
                    jarFiles.Add(file);
                }
                else if (fileName.EndsWith(".class"))
                {
                    classFiles.Add(file);
                }
            }

            var jobs = new List<DecompileJob>();
            var lockObj = new object();
            var extractionTasks = new List<Func<Task>>();

            foreach (var jarFile in jarFiles)
            {
                var jarName = jarFile.Name;
                var dirName = Path.GetFileNameWithoutExtension(jarName);
                var extractDir = new DirectoryInfo(Path.Combine(outputDir.FullName, dirName));

                extractionTasks.Add(async () =>
                {
                    try
                    {
                        await JarUtil.ExtractJarAsync(jarFile, extractDir);
                        var jarClassFiles = extractDir.GetFiles("*.class", SearchOption.AllDirectories);

                        lock (lockObj)
                        {
                            foreach (var classFile in jarClassFiles)
                            {
                                jobs.Add(new DecompileJob(classFile, classFile.DirectoryName ?? string.Empty, null));
                            }
                        }
                    }
                    catch
                    {
                    }
                });
            }

            if (extractionTasks.Count > 0)
                await taskManager.ExecuteParallelAsync(extractionTasks);

            foreach (var classFile in classFiles)
            {
                var relativePath = GetRelativePath(directory.FullName, classFile.DirectoryName ?? string.Empty);
                var targetDir = new DirectoryInfo(Path.Combine(outputDir.FullName, relativePath));

                if (!targetDir.Exists)
                    targetDir.Create();

                var targetFile = new FileInfo(Path.Combine(targetDir.FullName, classFile.Name));
                File.Copy(classFile.FullName, targetFile.FullName, true);

                jobs.Add(new DecompileJob(targetFile, targetDir.FullName, null));
            }

            return jobs;
        }

        public static async Task<List<DecompileJob>> ProcessJarFileAsync(FileInfo jarFile, string outputPath)
        {
            if (!jarFile.Exists)
                throw new FileNotFoundException($"文件不存在:{jarFile.FullName}");

            var outputDir = new DirectoryInfo(outputPath);
            if (!outputDir.Exists)
                outputDir.Create();

            var jarName = jarFile.Name;
            var dirName = Path.GetFileNameWithoutExtension(jarName);
            var extractDir = new DirectoryInfo(Path.Combine(outputDir.FullName, dirName));

            await JarUtil.ExtractJarAsync(jarFile, extractDir);

            var classFiles = extractDir.GetFiles("*.class", SearchOption.AllDirectories);
            var jobs = new List<DecompileJob>();

            foreach (var classFile in classFiles)
            {
                jobs.Add(new DecompileJob(classFile, classFile.DirectoryName ?? string.Empty, null));
            }

            return jobs;
        }

        public static async Task<List<DecompileJob>> ProcessClassFileAsync(FileInfo classFile, string outputPath)
        {
            if (!classFile.Exists)
                throw new FileNotFoundException($"文件不存在:{classFile.FullName}");

            var outputDir = new DirectoryInfo(outputPath);
            if (!outputDir.Exists)
                outputDir.Create();

            var targetFile = new FileInfo(Path.Combine(outputDir.FullName, classFile.Name));
            File.Copy(classFile.FullName, targetFile.FullName, true);

            var jobs = new List<DecompileJob>
            {
                new DecompileJob(targetFile, outputDir.FullName, null)
            };

            return await Task.FromResult(jobs);
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            var baseUri = new Uri(basePath);
            var fullUri = new Uri(fullPath + Path.DirectorySeparatorChar);
            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
        }
    }

    public static class JarUtil
    {
        public static bool IsJarFile(FileInfo file)
        {
            if (file == null || !file.Exists || file.Length == 0)
                return false;

            var name = file.Name.ToLower();
            return name.EndsWith(".jar") || name.EndsWith(".war") ||
                   name.EndsWith(".ear") || name.EndsWith(".aar");
        }

        public static async Task ExtractJarAsync(FileInfo jarFile, DirectoryInfo outputDir)
        {
            if (jarFile == null || !jarFile.Exists || jarFile.Length == 0)
            {
                return;
            }

            if (!outputDir.Exists)
                outputDir.Create();

            try
            {
                using (var archive = ZipFile.OpenRead(jarFile.FullName))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var entryName = entry.FullName;
                        var outFile = new FileInfo(Path.Combine(outputDir.FullName, entryName));

                        if (entryName.EndsWith("/") || string.IsNullOrEmpty(entry.Name))
                        {
                            outFile.Directory?.Create();
                        }
                        else
                        {
                            outFile.Directory?.Create();
                            entry.ExtractToFile(outFile.FullName, true);
                        }
                    }
                }
            }
            catch
            {
                throw;
            }
        }
    }

    public static class VineflowerInvoker
    {
        private static readonly string VineflowerJarUrl = "https://github.com/Vineflower/vineflower/releases/download/1.10.1/vineflower-1.10.1.jar";
        private static readonly string LocalJarPath = Path.Combine(Path.GetTempPath(), "supertoolbox_temp", "vineflower.jar");
        private static readonly SemaphoreSlim DownloadLock = new SemaphoreSlim(1, 1);
        private static bool _isDownloaded = false;
        private static readonly HttpClient _httpClient = new HttpClient();

        static VineflowerInvoker()
        {
            string? directoryPath = Path.GetDirectoryName(LocalJarPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            LoadEmbeddedJar();
        }

        private static void LoadEmbeddedJar()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string[] resourceNames = assembly.GetManifestResourceNames();
                string? jarResource = resourceNames.FirstOrDefault(r => r.EndsWith("vineflower.jar") || r.EndsWith("vineflower-1.10.1.jar"));

                if (jarResource != null)
                {
                    using var stream = assembly.GetManifestResourceStream(jarResource);
                    if (stream != null)
                    {
                        string? directoryPath = Path.GetDirectoryName(LocalJarPath);
                        if (!string.IsNullOrEmpty(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }
                        using var fileStream = new FileStream(LocalJarPath, FileMode.Create, FileAccess.Write);
                        stream.CopyTo(fileStream);
                        _isDownloaded = true;
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        public static async Task<(bool Success, string? ErrorMessage)> DecompileAsync(string inputFile, string outputDir, Dictionary<string, object> options)
        {
            try
            {
                await EnsureVineflowerJarAsync();

                var args = BuildArguments(inputFile, outputDir, options);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "java",
                        Arguments = $"-jar \"{LocalJarPath}\" {args}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(output))
                {
                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        Console.WriteLine(line);
                    }
                }

                return process.ExitCode == 0
                    ? (true, null)
                    : (false, string.IsNullOrEmpty(error) ? "未知错误" : error);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static async Task EnsureVineflowerJarAsync()
        {
            if (_isDownloaded && File.Exists(LocalJarPath))
                return;

            await DownloadLock.WaitAsync();
            try
            {
                if (_isDownloaded && File.Exists(LocalJarPath))
                    return;

                string? directoryPath = Path.GetDirectoryName(LocalJarPath);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                using (var response = await _httpClient.GetAsync(VineflowerJarUrl))
                using (var fs = new FileStream(LocalJarPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                _isDownloaded = true;
            }
            finally
            {
                DownloadLock.Release();
            }
        }

        private static string BuildArguments(string inputFile, string outputDir, Dictionary<string, object> options)
        {
            var sb = new StringBuilder();

            foreach (var opt in options)
            {
                if (opt.Value != null)
                {
                    sb.Append($" -{opt.Key}={opt.Value}");
                }
            }

            sb.Append($" \"{inputFile}\"");
            sb.Append($" \"{outputDir}\"");

            return sb.ToString();
        }
    }
}