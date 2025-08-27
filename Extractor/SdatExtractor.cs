using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace super_toolbox
{
    public class SdatExtractor : BaseExtractor
    {
        private readonly object _lockObject = new object();
        private int _processedFiles = 0;

        public new event EventHandler<string>? FileExtracted;
        public event EventHandler<string>? ExtractionProgress;

        private readonly string[] _supportedMapResources = {
            "BGM_map.txt",
            "BtlVoice_map.txt",
            "DataBase_map.txt",
            "DataBase2_map.txt",
            "Exi_UT2_map.txt",
            "Grp1_map.txt",
            "Grp2_map.txt",
            "Grp3_map.txt",
            "Model_map.txt",
            "PatternFade_map.txt",
            "ScriptEvent_map.txt",
            "SE_map.txt",
            "Voice_map.txt"
        };

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnError($"目录不存在: {directoryPath}");
                return;
            }

            var sdatFiles = Directory.EnumerateFiles(directoryPath, "*.sdat", SearchOption.AllDirectories)
                .ToList();

            sdatFiles = sdatFiles.Where(file =>
                _supportedMapResources.Contains(Path.GetFileNameWithoutExtension(file) + "_map.txt",
                StringComparer.OrdinalIgnoreCase)).ToList();

            TotalFilesToExtract = sdatFiles.Count;
            OnProgress($"开始处理 {sdatFiles.Count} 个SDAT文件");

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            await Task.Run(async () =>
            {
                foreach (var sdatFile in sdatFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        Interlocked.Increment(ref _processedFiles);
                        string fileName = Path.GetFileName(sdatFile);
                        OnProgress($"正在处理 {_processedFiles}/{sdatFiles.Count}: {fileName}");

                        if (Path.GetFileNameWithoutExtension(sdatFile).Equals("Grp1", StringComparison.OrdinalIgnoreCase))
                        {
                            await ProcessLargeSdatFileAsync(sdatFile, extractedDir, cancellationToken);
                        }
                        else
                        {
                            await ProcessSingleSdatFileAsync(sdatFile, extractedDir, cancellationToken);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        OnError($"处理失败: {Path.GetFileName(sdatFile)} - {ex.Message}");
                    }
                }
            }, cancellationToken);

            OnExtractionCompleted();
        }

        private async Task ProcessLargeSdatFileAsync(string sdatFilePath, string outputDir, CancellationToken ct)
        {
            string mapResourceName = Path.GetFileNameWithoutExtension(sdatFilePath) + "_map.txt";
            string mapContent = LoadEmbeddedTextResource(mapResourceName);
            var offsetMap = ParseOffsetMap(mapContent);
            string sdatName = Path.GetFileNameWithoutExtension(sdatFilePath);
            string sdatOutputDir = Path.Combine(outputDir, sdatName);
            Directory.CreateDirectory(sdatOutputDir);

            using (var fileStream = new FileStream(sdatFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096 * 1024, useAsync: true))
            {
                foreach (var entry in offsetMap)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (entry.Offset < 0 || entry.Size < 0 || entry.Offset > fileStream.Length || entry.Offset + entry.Size > fileStream.Length)
                        {
                            OnError($"无效的文件偏移或大小: {entry.FileName} (偏移: 0x{entry.Offset:X}, 大小: 0x{entry.Size:X}, SDAT大小: 0x{fileStream.Length:X})");
                            continue;
                        }

                        string outputFilePath = Path.Combine(sdatOutputDir, entry.FileName);
                        string? outputFileDir = Path.GetDirectoryName(outputFilePath);
                        if (!string.IsNullOrEmpty(outputFileDir))
                            Directory.CreateDirectory(outputFileDir);

                        fileStream.Seek(entry.Offset, SeekOrigin.Begin);
                        await Task.Yield();

                        using (var outputStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096 * 1024, useAsync: true))
                        {
                            byte[] buffer = new byte[4096 * 1024];
                            long bytesRemaining = entry.Size;

                            while (bytesRemaining > 0)
                            {
                                ct.ThrowIfCancellationRequested();

                                int bytesToRead = (int)Math.Min(buffer.Length, bytesRemaining);
                                int bytesRead = await fileStream.ReadAsync(buffer, 0, bytesToRead, ct);

                                if (bytesRead == 0)
                                    break;

                                await outputStream.WriteAsync(buffer, 0, bytesRead, ct);
                                bytesRemaining -= bytesRead;
                            }
                        }

                        string relativePath = Path.Combine(sdatName, entry.FileName);
                        base.OnFileExtracted(relativePath);
                        FileExtracted?.Invoke(this, $"已提取: {relativePath} (偏移: 0x{entry.Offset:X}, 大小: 0x{entry.Size:X})");
                    }
                    catch (Exception ex)
                    {
                        OnError($"提取文件失败: {entry.FileName} - {ex.Message}");
                    }
                }
            }
        }

        private async Task ProcessSingleSdatFileAsync(string sdatFilePath, string outputDir, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                string mapResourceName = Path.GetFileNameWithoutExtension(sdatFilePath) + "_map.txt";
                string mapContent = LoadEmbeddedTextResource(mapResourceName);
                var offsetMap = ParseOffsetMap(mapContent);
                byte[] sdatData = File.ReadAllBytes(sdatFilePath);
                ExtractFilesFromSdat(sdatData, offsetMap, outputDir, Path.GetFileNameWithoutExtension(sdatFilePath), ct);
            }, ct);
        }

        private string LoadEmbeddedTextResource(string resourceName)
        {
            var assembly = typeof(SdatExtractor).Assembly;
            string fullResourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"嵌入的TXT资源 '{resourceName}' 未找到", resourceName);

            Stream? stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
                throw new FileNotFoundException($"无法打开嵌入的TXT资源流 '{resourceName}'", resourceName);

            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private OffsetMapEntry[] ParseOffsetMap(string mapContent)
        {
            var lines = mapContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inEntries = false;
            var entries = new System.Collections.Generic.List<OffsetMapEntry>();

            foreach (var line in lines)
            {
                if (line == "BEGIN_ENTRIES")
                {
                    inEntries = true;
                    continue;
                }
                if (line == "END_ENTRIES")
                {
                    inEntries = false;
                    break;
                }
                if (inEntries)
                {
                    var parts = new System.Collections.Generic.List<string>();
                    int colonCount = 0;
                    var currentPart = new StringBuilder();

                    foreach (char c in line)
                    {
                        if (c == ':' && colonCount < 2)
                        {
                            parts.Add(currentPart.ToString());
                            currentPart.Clear();
                            colonCount++;
                        }
                        else if (c == '\\' && colonCount < 2 && currentPart.Length > 0 && currentPart[currentPart.Length - 1] == '\\')
                        {
                            currentPart.Remove(currentPart.Length - 1, 1);
                            currentPart.Append(c);
                        }
                        else if (c == 'n' && colonCount < 2 && currentPart.Length > 0 && currentPart[currentPart.Length - 1] == '\\')
                        {
                            currentPart.Remove(currentPart.Length - 1, 1);
                            currentPart.Append('\n');
                        }
                        else if (c == 'r' && colonCount < 2 && currentPart.Length > 0 && currentPart[currentPart.Length - 1] == '\\')
                        {
                            currentPart.Remove(currentPart.Length - 1, 1);
                            currentPart.Append('\r');
                        }
                        else if (c == ':' && colonCount < 2 && currentPart.Length > 0 && currentPart[currentPart.Length - 1] == '\\')
                        {
                            currentPart.Remove(currentPart.Length - 1, 1);
                            currentPart.Append(c);
                        }
                        else
                        {
                            currentPart.Append(c);
                        }
                    }

                    if (colonCount == 2)
                    {
                        parts.Add(currentPart.ToString());
                    }

                    if (parts.Count == 3)
                    {
                        string fileName = parts[0]
                            .Replace("\\n", "\n")
                            .Replace("\\r", "\r")
                            .Replace("\\:", ":");

                        if (long.TryParse(parts[1], out long offset) && long.TryParse(parts[2], out long size))
                        {
                            entries.Add(new OffsetMapEntry
                            {
                                Index = entries.Count,
                                FileName = fileName,
                                Offset = offset,
                                Size = size
                            });
                        }
                    }
                }
            }

            return entries.ToArray();
        }

        private void ExtractFilesFromSdat(byte[] sdatData, OffsetMapEntry[] offsetMap, string baseOutputDir, string sdatName, CancellationToken ct)
        {
            string sdatOutputDir = Path.Combine(baseOutputDir, sdatName);
            Directory.CreateDirectory(sdatOutputDir);

            foreach (var entry in offsetMap)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (entry.Offset < 0 || entry.Offset >= sdatData.Length || entry.Offset + entry.Size > sdatData.Length)
                    {
                        OnError($"无效的文件偏移或大小: {entry.FileName} (偏移: 0x{entry.Offset:X}, 大小: 0x{entry.Size:X}, SDAT大小: 0x{sdatData.Length:X})");
                        continue;
                    }

                    byte[] fileData = new byte[entry.Size];
                    Buffer.BlockCopy(sdatData, (int)entry.Offset, fileData, 0, (int)entry.Size);

                    string outputFilePath = Path.Combine(sdatOutputDir, entry.FileName);
                    string? outputFileDir = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(outputFileDir))
                        Directory.CreateDirectory(outputFileDir);

                    File.WriteAllBytes(outputFilePath, fileData);

                    string relativePath = Path.Combine(sdatName, entry.FileName);
                    base.OnFileExtracted(relativePath);
                    FileExtracted?.Invoke(this, $"已提取: {relativePath} (偏移: 0x{entry.Offset:X}, 大小: 0x{entry.Size:X})");
                }
                catch (Exception ex)
                {
                    OnError($"提取文件失败: {entry.FileName} - {ex.Message}");
                }
            }
        }

        private void OnProgress(string message)
        {
            ExtractionProgress?.Invoke(this, message);
        }

        private void OnError(string error)
        {
            ExtractionProgress?.Invoke(this, $"错误: {error}");
        }

        private class OffsetMapEntry
        {
            public int Index { get; set; }
            public string FileName { get; set; } = string.Empty;
            public long Offset { get; set; }
            public long Size { get; set; }
        }
    }
}
