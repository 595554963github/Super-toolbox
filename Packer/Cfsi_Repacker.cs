using System.IO.Compression;
using System.Text.Json;

namespace super_toolbox
{
    public class Cfsi_Repacker : BaseExtractor
    {
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        public override void Extract(string directoryPath)
        {
            RepackAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await RepackAsync(directoryPath, cancellationToken);
        }

        public async Task RepackAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                PackingError?.Invoke(this, "目录路径不能为空");
                OnPackingFailed("目录路径不能为空");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, $"目录不存在:{directoryPath}");
                OnPackingFailed($"目录不存在:{directoryPath}");
                return;
            }

            PackingStarted?.Invoke(this, $"开始打包目录:{directoryPath}");

            try
            {
                string jsonFilePath = FindJsonFile(directoryPath);
                if (string.IsNullOrEmpty(jsonFilePath))
                {
                    PackingError?.Invoke(this, "未找到CFSI结构文件(.json)，无法进行打包");
                    OnPackingFailed("未找到CFSI结构文件(.json)，无法进行打包");
                    return;
                }

                PackingProgress?.Invoke(this, $"找到结构文件:{Path.GetFileName(jsonFilePath)}");

                var structureInfo = await ReadStructureJson(jsonFilePath);
                if (structureInfo == null)
                {
                    PackingError?.Invoke(this, "无法读取结构文件内容");
                    OnPackingFailed("无法读取结构文件内容");
                    return;
                }

                string outputCfsiPath = GetOutputCfsiPath(directoryPath, jsonFilePath);
                string extractedDir = GetExtractedDir(directoryPath, jsonFilePath);

                if (!Directory.Exists(extractedDir))
                {
                    PackingError?.Invoke(this, $"提取的文件夹不存在:{extractedDir}");
                    OnPackingFailed($"提取的文件夹不存在:{extractedDir}");
                    return;
                }

                PackingProgress?.Invoke(this, $"输出CFSI文件:{Path.GetFileName(outputCfsiPath)}");
                PackingProgress?.Invoke(this, $"源文件夹:{Path.GetFileName(extractedDir)}");

                TotalFilesToPack = structureInfo.Entries?.Length ?? 0;

                await RepackFromStructure(extractedDir, outputCfsiPath, structureInfo, cancellationToken);

                PackingProgress?.Invoke(this, $"打包完成: {Path.GetFileName(outputCfsiPath)}");
                OnPackingCompleted();
            }
            catch (OperationCanceledException)
            {
                PackingError?.Invoke(this, "打包操作已取消");
                OnPackingFailed("打包操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"打包失败:{ex.Message}");
                OnPackingFailed($"打包失败:{ex.Message}");
            }
        }

        private string FindJsonFile(string directoryPath)
        {
            string folderName = Path.GetFileName(directoryPath);
            string jsonPath = Path.Combine(Directory.GetParent(directoryPath)?.FullName ?? directoryPath, folderName + ".json");

            if (File.Exists(jsonPath))
            {
                return jsonPath;
            }

            var jsonFiles = Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly);
            if (jsonFiles.Length > 0)
            {
                return jsonFiles[0];
            }

            return string.Empty;
        }

        private string GetOutputCfsiPath(string directoryPath, string jsonFilePath)
        {
            string jsonFileName = Path.GetFileNameWithoutExtension(jsonFilePath);
            string parentDir = Path.GetDirectoryName(jsonFilePath) ?? directoryPath;
            return Path.Combine(parentDir, jsonFileName + ".cfsi");
        }

        private string GetExtractedDir(string directoryPath, string jsonFilePath)
        {
            string jsonFileName = Path.GetFileNameWithoutExtension(jsonFilePath);
            string parentDir = Path.GetDirectoryName(jsonFilePath) ?? directoryPath;
            return Path.Combine(parentDir, jsonFileName);
        }

        private async Task<CfsiStructureInfo?> ReadStructureJson(string jsonFilePath)
        {
            try
            {
                string jsonContent = await File.ReadAllTextAsync(jsonFilePath);
                return JsonSerializer.Deserialize<CfsiStructureInfo>(jsonContent);
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"读取结构文件失败:{ex.Message}");
                return null;
            }
        }

        private async Task RepackFromStructure(string extractedDir, string outputCfsiPath, CfsiStructureInfo structureInfo, CancellationToken cancellationToken)
        {
            using (var fs = new FileStream(outputCfsiPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                var folderGroups = structureInfo.Entries?
                    .GroupBy(e =>
                    {
                        string dir = Path.GetDirectoryName(e.FullPath)?.Replace("\\", "/") ?? "";
                        return string.IsNullOrEmpty(dir) ? "" : dir + "/";
                    })
                    .ToList() ?? new List<IGrouping<string, CfsiEntryInfo>>();

                WriteNum(writer, folderGroups.Count);

                foreach (var folderGroup in folderGroups)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string folderPath = folderGroup.Key;
                    WriteString(writer, folderPath);
                    WriteNum(writer, folderGroup.Count());

                    foreach (var entry in folderGroup)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        string fileName = Path.GetFileName(entry.FullPath);
                        WriteString(writer, fileName);

                        uint offsetValue = entry.Offset / 0x10;
                        writer.Write(offsetValue);
                        writer.Write(entry.Size);

                        PackingProgress?.Invoke(this, $"添加文件:{entry.FullPath}");
                        OnFilePacked(entry.FullPath);
                    }
                }

                long currentPos = fs.Position;
                long alignedPos = (currentPos + 0x0F) & ~0x0F;
                while (fs.Position < alignedPos)
                {
                    writer.Write((byte)0);
                }

                long dataSectionStart = fs.Position;

                var fileDataList = new List<(CfsiEntryInfo entry, byte[] data)>();
                foreach (var entry in structureInfo.Entries ?? Array.Empty<CfsiEntryInfo>())
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string filePath = FindActualFilePath(extractedDir, entry);

                    if (!File.Exists(filePath))
                    {
                        PackingError?.Invoke(this, $"文件不存在:{filePath}");
                        fileDataList.Add((entry, new byte[0]));
                        continue;
                    }

                    byte[] fileData = await File.ReadAllBytesAsync(filePath);

                    if (entry.Size != (uint)fileData.Length)
                    {
                        fileData = await CompressFileData(fileData);
                    }

                    fileDataList.Add((entry, fileData));
                }

                foreach (var (entry, fileData) in fileDataList.OrderBy(x => x.entry.Offset))
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    long filePos = dataSectionStart + entry.Offset;
                    if (fs.Position != filePos)
                    {
                        fs.Position = filePos;
                    }

                    writer.Write(fileData);

                    long fileEnd = fs.Position;
                    long alignedEnd = (fileEnd + 0x0F) & ~0x0F;
                    while (fs.Position < alignedEnd)
                    {
                        writer.Write((byte)0);
                    }

                    PackingProgress?.Invoke(this, $"写入数据:{entry.FullPath} ({fileData.Length}字节)");
                }

                long finalPos = fs.Position;
                long alignedFinal = (finalPos + 0x0F) & ~0x0F;
                while (fs.Position < alignedFinal)
                {
                    writer.Write((byte)0);
                }
            }
        }

        private string FindActualFilePath(string extractedDir, CfsiEntryInfo entry)
        {
            string originalPath = Path.Combine(extractedDir, entry.FullPath);

            if (File.Exists(originalPath))
            {
                return originalPath;
            }

            if (!string.IsNullOrEmpty(entry.OutputPath) && File.Exists(entry.OutputPath))
            {
                return entry.OutputPath;
            }

            string directory = Path.GetDirectoryName(originalPath) ?? extractedDir;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(entry.FullPath);
            string extension = Path.GetExtension(entry.FullPath);

            var filesInDir = Directory.GetFiles(directory, $"{fileNameWithoutExt}*{extension}");
            if (filesInDir.Length > 0)
            {
                return filesInDir[0];
            }

            return originalPath;
        }

        private async Task<byte[]> CompressFileData(byte[] data)
        {
            using (var compressedStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress, true))
                {
                    await gzipStream.WriteAsync(data, 0, data.Length);
                }

                var result = new byte[compressedStream.Length + 4];
                BitConverter.GetBytes((uint)data.Length).CopyTo(result, 0);
                Array.Copy(compressedStream.GetBuffer(), 0, result, 4, (int)compressedStream.Length);

                return result;
            }
        }

        private void WriteNum(BinaryWriter writer, int value)
        {
            if (value < 0xF8)
            {
                writer.Write((byte)value);
            }
            else
            {
                writer.Write((byte)0xFC);
                writer.Write((ushort)value);
            }
        }

        private void WriteString(BinaryWriter writer, string text)
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);
            if (bytes.Length < 0xF8)
            {
                writer.Write((byte)bytes.Length);
            }
            else
            {
                writer.Write((byte)0xFC);
                writer.Write((ushort)bytes.Length);
            }
            writer.Write(bytes);
        }

        public void Repack(string directoryPath)
        {
            RepackAsync(directoryPath).Wait();
        }
    }

    internal class CfsiStructureInfo
    {
        public string? SourceCfsiFile { get; set; }
        public string? ExtractionDate { get; set; }
        public int TotalEntries { get; set; }
        public int ExtractedEntries { get; set; }
        public CfsiEntryInfo[]? Entries { get; set; }
    }

    internal class CfsiEntryInfo
    {
        public string FullPath { get; set; } = string.Empty;
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public bool Extracted { get; set; }
        public string OutputPath { get; set; } = string.Empty;
    }
}
