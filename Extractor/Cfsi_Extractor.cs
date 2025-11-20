using System.IO.Compression;
using System.Text.Json;

namespace super_toolbox
{
    public class Cfsi_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                ExtractionError?.Invoke(this, "目录路径不能为空");
                OnExtractionFailed("目录路径不能为空");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => Path.GetExtension(file).ToLower() != ".extracted");

            int totalExtractedFromCfsi = 0;
            int processedCfsiFiles = 0;

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);

                ExtractionProgress?.Invoke(this, $"正在处理CFSI文件:{Path.GetFileName(filePath)}");

                try
                {
                    int extractedCount = await ProcessCfsiFile(filePath, cancellationToken);
                    totalExtractedFromCfsi += extractedCount;
                    processedCfsiFiles++;

                    if (extractedCount > 0)
                    {
                        ExtractionProgress?.Invoke(this, $"从CFSI文件{Path.GetFileName(filePath)}中提取出{extractedCount}个文件");
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理CFSI文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"处理CFSI文件{filePath}时出错:{e.Message}");
                }
            }

            if (totalExtractedFromCfsi > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共从{processedCfsiFiles}个CFSI文件中提取出{totalExtractedFromCfsi}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到有效的CFSI文件");
            }

            OnExtractionCompleted();
        }

        private async Task<int> ProcessCfsiFile(string filePath, CancellationToken cancellationToken)
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string parentDir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string extractedDir = Path.Combine(parentDir, fileNameWithoutExt);

            Directory.CreateDirectory(extractedDir);

            List<string> extractedFiles = new List<string>();
            List<CfsiFileEntry> fileEntries = new List<CfsiFileEntry>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                int folders = GetNum(reader);

                for (int folder = 0; folder < folders; folder++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    int pathSize = GetNum(reader);
                    string path = new string(reader.ReadChars(pathSize));

                    int files = GetNum(reader);

                    for (int i = 0; i < files; i++)
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        int nameSize = GetNum(reader);
                        string name = new string(reader.ReadChars(nameSize));

                        uint offset = reader.ReadUInt32();
                        uint size = reader.ReadUInt32();

                        offset *= 0x10;

                        string fullPath = path + name;

                        var entry = new CfsiFileEntry
                        {
                            FullPath = fullPath,
                            Offset = offset,
                            Size = size
                        };

                        fileEntries.Add(entry);
                    }
                }

                long baseOffset = fs.Position;
                baseOffset = (baseOffset + 0x0F) & ~0x0F; 

                foreach (var entry in fileEntries)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    bool success = await ExtractFileEntry(fs, entry, baseOffset, extractedDir, extractedFiles);
                    if (success)
                    {
                        entry.Extracted = true;
                        entry.OutputPath = GetOutputFilePath(extractedDir, entry.FullPath, extractedFiles);
                    }
                }
            }

            await GenerateStructureJson(filePath, parentDir, fileEntries);

            return extractedFiles.Count;
        }

        private async Task<bool> ExtractFileEntry(FileStream fs, CfsiFileEntry entry, long baseOffset, string extractedDir, List<string> extractedFiles)
        {
            long actualOffset = baseOffset + entry.Offset;

            if (actualOffset + entry.Size > fs.Length)
            {
                ExtractionError?.Invoke(this, $"文件{entry.FullPath}超出文件范围");
                return false;
            }

            fs.Seek(actualOffset, SeekOrigin.Begin);

            using (var reader = new BinaryReader(fs, System.Text.Encoding.Default, true))
            {
                string outputFilePath = GetOutputFilePath(extractedDir, entry.FullPath, extractedFiles);
                string outputDir = Path.GetDirectoryName(outputFilePath) ?? string.Empty;

                if (string.IsNullOrEmpty(outputDir))
                {
                    ExtractionError?.Invoke(this, $"无法确定输出目录:{entry.FullPath}");
                    return false;
                }

                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                if (entry.Size < 6)
                {
                    byte[] data = reader.ReadBytes((int)entry.Size);
                    await File.WriteAllBytesAsync(outputFilePath, data);
                }
                else
                {
                    uint uncompressedSize = reader.ReadUInt32();
                    ushort gzipSignature = reader.ReadUInt16();

                    if (gzipSignature == 0x8B1F)
                    {
                        long compressedOffset = actualOffset + 4;
                        uint compressedSize = entry.Size - 4;

                        await ExtractCompressedFile(fs, compressedOffset, compressedSize, uncompressedSize, outputFilePath);
                    }
                    else
                    {
                        fs.Seek(-6, SeekOrigin.Current);
                        byte[] data = reader.ReadBytes((int)entry.Size);
                        await File.WriteAllBytesAsync(outputFilePath, data);
                    }
                }

                if (!extractedFiles.Contains(outputFilePath))
                {
                    extractedFiles.Add(outputFilePath);
                    OnFileExtracted(outputFilePath);
                }

                return true;
            }
        }

        private string GetOutputFilePath(string baseDir, string fullPath, List<string> existingFiles)
        {
            string outputFilePath = Path.Combine(baseDir, fullPath);

            if (File.Exists(outputFilePath) || existingFiles.Contains(outputFilePath))
            {
                string directory = Path.GetDirectoryName(outputFilePath) ?? baseDir;
                string fileName = Path.GetFileNameWithoutExtension(fullPath);
                string extension = Path.GetExtension(fullPath);

                int counter = 1;
                string newFilePath;

                do
                {
                    newFilePath = Path.Combine(directory, $"{fileName}_{counter}{extension}");
                    counter++;
                } while (File.Exists(newFilePath) || existingFiles.Contains(newFilePath));

                return newFilePath;
            }

            return outputFilePath;
        }

        private async Task ExtractCompressedFile(FileStream fs, long offset, uint compressedSize, uint uncompressedSize, string outputFilePath)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            byte[] compressedData = new byte[compressedSize];
            await fs.ReadAsync(compressedData, 0, (int)compressedSize);

            try
            {
                using (var compressedStream = new MemoryStream(compressedData))
                using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
                using (var outputStream = new FileStream(outputFilePath, FileMode.Create))
                {
                    await gzipStream.CopyToAsync(outputStream);
                }
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"解压文件{outputFilePath} 时出错:{ex.Message}");
                await File.WriteAllBytesAsync(outputFilePath + ".compressed", compressedData);
            }
        }

        private async Task GenerateStructureJson(string cfsiFilePath, string outputDir, List<CfsiFileEntry> fileEntries)
        {
            try
            {
                var structureInfo = new
                {
                    SourceCfsiFile = Path.GetFileName(cfsiFilePath),
                    ExtractionDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    TotalEntries = fileEntries.Count,
                    ExtractedEntries = fileEntries.Count(e => e.Extracted),
                    Entries = fileEntries.Select(e => new
                    {
                        e.FullPath,
                        e.Offset,
                        e.Size,
                        e.Extracted,
                        OutputPath = e.OutputPath ?? string.Empty
                    }).ToArray()
                };

                string jsonFilePath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(cfsiFilePath) + ".json");
                string json = JsonSerializer.Serialize(structureInfo, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                await File.WriteAllTextAsync(jsonFilePath, json);
                ExtractionProgress?.Invoke(this, $"已生成结构文件:{Path.GetFileName(jsonFilePath)}");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"生成JSON结构文件时出错:{ex.Message}");
            }
        }
        private int GetNum(BinaryReader reader)
        {
            byte firstByte = reader.ReadByte();

            if (firstByte == 0xF8 || firstByte == 0xFC)
            {
                return reader.ReadUInt16();
            }
            else
            {
                return firstByte;
            }
        }
    }
    internal class CfsiFileEntry
    {
        public string FullPath { get; set; } = string.Empty;
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public bool Extracted { get; set; }
        public string? OutputPath { get; set; }
    }
}
