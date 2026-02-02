namespace super_toolbox
{
    public class PlayStation_Trp_Extractor : BaseExtractor
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
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var trpFiles = Directory.EnumerateFiles(directoryPath, "*.trp", SearchOption.AllDirectories).ToList();

            if (trpFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"未找到.trp文件");
                OnExtractionFailed($"未找到.trp文件");
                return;
            }

            TotalFilesToExtract = trpFiles.Count;
            ExtractionProgress?.Invoke(this, $"找到{trpFiles.Count}个.trp文件,开始提取...");

            int processedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                foreach (var filePath in trpFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processedCount++;

                    string fileName = Path.GetFileName(filePath);
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{trpFiles.Count}): {fileName}");

                    try
                    {
                        int extractedCount = await ExtractTrpFile(filePath, cancellationToken);
                        totalExtractedFiles += extractedCount;
                        ExtractionProgress?.Invoke(this, $"{fileName}提取完成,共提取{extractedCount}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{fileName}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"所有.trp文件处理完成,总共提取{totalExtractedFiles}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程中出错:{ex.Message}");
                OnExtractionFailed($"提取过程中出错:{ex.Message}");
            }
        }

        private async Task<int> ExtractTrpFile(string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                ExtractionError?.Invoke(this, $"文件不存在{filePath}");
                return 0;
            }

            string basename = Path.GetFileNameWithoutExtension(filePath);
            string outputDir = Path.Combine(Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory, basename);

            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
                await Task.Delay(300, cancellationToken);
            }

            Directory.CreateDirectory(outputDir);
            ExtractionProgress?.Invoke(this, $"正在解包:{Path.GetFileName(filePath)}");
            ExtractionProgress?.Invoke(this, $"输出到:{outputDir}");

            int extractedCount = 0;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    byte[] header = reader.ReadBytes(4);
                    uint magic = BitConverter.ToUInt32(header, 0);

                    if (magic != 0x004DA2DC)
                    {
                        ExtractionError?.Invoke(this, $"不是有效的TRP文件:{Path.GetFileName(filePath)}");
                        return 0;
                    }

                    fs.Seek(0x40, SeekOrigin.Begin);
                    long firstDataOffset = -1;
                    var fileEntries = new List<FileEntry>();

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (fs.Position + 64 > fs.Length) break;

                        long entryStart = fs.Position;
                        byte[] entryData = reader.ReadBytes(64);
                        if (entryData.Length < 64) break;

                        string fileName = "";
                        for (int i = 0; i < 32; i++)
                        {
                            if (entryData[i] == 0) break;
                            fileName += (char)entryData[i];
                        }

                        if (string.IsNullOrEmpty(fileName)) break;

                        uint offset = (uint)((entryData[0x24] << 24) | (entryData[0x25] << 16) | (entryData[0x26] << 8) | entryData[0x27]);
                        uint size = (uint)((entryData[0x2C] << 24) | (entryData[0x2D] << 16) | (entryData[0x2E] << 8) | entryData[0x2F]);

                        if (firstDataOffset == -1 && offset > 0)
                        {
                            firstDataOffset = offset;
                        }

                        if (offset == 0 || size == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"跳过文件{fileName}:偏移或大小为0");
                            continue;
                        }

                        if (entryStart >= firstDataOffset && firstDataOffset > 0)
                        {
                            break;
                        }

                        fileEntries.Add(new FileEntry
                        {
                            Name = fileName,
                            Offset = offset,
                            Size = size
                        });
                    }

                    ExtractionProgress?.Invoke(this, $"找到{fileEntries.Count}个有效文件条目");

                    foreach (var entry in fileEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (entry.Offset >= fs.Length)
                        {
                            ExtractionError?.Invoke(this, $"文件{entry.Name}偏移地址超出文件范围:{entry.Offset:X8}");
                            continue;
                        }

                        if (entry.Offset + entry.Size > fs.Length)
                        {
                            ExtractionError?.Invoke(this, $"文件{entry.Name}大小超出文件范围:偏移{entry.Offset:X8},大小{entry.Size}");
                            continue;
                        }

                        fs.Seek(entry.Offset, SeekOrigin.Begin);
                        byte[] data = reader.ReadBytes((int)entry.Size);

                        if (data.Length == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"文件{entry.Name}:数据长度为0");
                            continue;
                        }

                        string outputFile = Path.Combine(outputDir, entry.Name);

                        string? outputFileDir = Path.GetDirectoryName(outputFile);
                        if (!string.IsNullOrEmpty(outputFileDir) && !Directory.Exists(outputFileDir))
                        {
                            Directory.CreateDirectory(outputFileDir);
                        }

                        await File.WriteAllBytesAsync(outputFile, data, cancellationToken);

                        extractedCount++;
                        double sizeKB = entry.Size / 1024.0;

                        ExtractionProgress?.Invoke(this, $"{entry.Name,-40}- {entry.Size,9}字节({sizeKB,7:F1}KB)");
                        OnFileExtracted(outputFile);
                    }
                }

                ExtractionProgress?.Invoke(this, $"解包完成!提取了{extractedCount}个文件到'{outputDir}'目录");

                if (extractedCount == 0)
                {
                    ExtractionError?.Invoke(this, $"警告:没有提取到任何文件!可能原因:文件格式不正确或已损坏");
                }

                return extractedCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取TRP文件时出错:{ex.Message}");
                throw new Exception($"提取TRP文件时出错:{ex.Message}", ex);
            }
        }
        private class FileEntry
        {
            public string Name = "";
            public uint Offset;
            public uint Size;
        }
    }
}
