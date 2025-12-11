namespace super_toolbox
{
    public class DyingLight_csb_Extractor : BaseExtractor
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

            var csbFiles = Directory.EnumerateFiles(directoryPath, "*.csb", SearchOption.AllDirectories)
                .ToList();

            if (csbFiles.Count == 0)
            {
                ExtractionError?.Invoke(this, $"未找到.csb文件");
                OnExtractionFailed($"未找到.csb文件");
                return;
            }

            TotalFilesToExtract = csbFiles.Count;
            ExtractionProgress?.Invoke(this, $"找到{csbFiles.Count}个.csb文件,开始提取...");

            int processedCount = 0;
            int totalExtractedFiles = 0;

            try
            {
                foreach (var filePath in csbFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    processedCount++;
                    string fileName = Path.GetFileName(filePath);
                    ExtractionProgress?.Invoke(this, $"正在处理文件({processedCount}/{csbFiles.Count}): {fileName}");

                    try
                    {
                        int extractedCount = await ExtractCsbFile(filePath, cancellationToken);
                        totalExtractedFiles += extractedCount;

                        ExtractionProgress?.Invoke(this, $"{fileName}提取完成,共提取{extractedCount}个文件");
                    }
                    catch (OperationCanceledException)
                    {
                        ExtractionError?.Invoke(this, "提取操作已取消");
                        OnExtractionFailed("提取操作已取消");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{fileName}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{fileName}时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"所有.csb文件处理完成,总共提取{totalExtractedFiles}个文件");
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

        private async Task<int> ExtractCsbFile(string filePath, CancellationToken cancellationToken)
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
                    fs.Seek(0x40, SeekOrigin.Begin);

                    uint infoSize = reader.ReadUInt32();
                    uint fileCount = reader.ReadUInt32();
                    reader.ReadUInt32();

                    ExtractionProgress?.Invoke(this, $"文件数量:{fileCount}");

                    var entries = new List<(string Name, uint Offset, uint Size)>();

                    for (int i = 0; i < fileCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        byte[] nameBytes = reader.ReadBytes(0x40);
                        int nullPos = Array.IndexOf(nameBytes, (byte)0);
                        string name = nullPos != -1
                            ? System.Text.Encoding.ASCII.GetString(nameBytes, 0, nullPos)
                            : $"file_{i}";

                        uint offset = reader.ReadUInt32();
                        uint size = reader.ReadUInt32();

                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt32();

                        entries.Add((name, offset, size));

                        if (i == 0)
                        {
                            ExtractionProgress?.Invoke(this, $"第一个文件偏移:0x{offset:X8},大小:0x{size:X8}");
                        }
                    }

                    foreach (var (name, offset, size) in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (size == 0 || offset == 0)
                            continue;

                        fs.Seek(offset, SeekOrigin.Begin);
                        byte[] data = reader.ReadBytes((int)size);

                        if (data.Length == 0)
                            continue;

                        string extension = data.Length >= 4 && data[0] == 'F' && data[1] == 'S' && data[2] == 'B' && data[3] == '5'
                            ? ".fsb"
                            : ".dat";

                        string outputFile = Path.Combine(outputDir, $"{name}{extension}");

                        await File.WriteAllBytesAsync(outputFile, data, cancellationToken);

                        extractedCount++;
                        double sizeKB = size / 1024.0;

                        ExtractionProgress?.Invoke(this, $"{name,-40}-{size,9} 字节({sizeKB,7:F1}kb)");
                        OnFileExtracted(outputFile);
                    }
                }

                ExtractionProgress?.Invoke(this, $"解包完成!提取了{extractedCount}个文件到'{outputDir}'目录");

                if (extractedCount == 0)
                {
                    ExtractionError?.Invoke(this, $"警告:没有提取到任何文件!可能原因: 文件格式不正确或已损坏");
                }

                return extractedCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取CSB文件时出错:{ex.Message}");
                throw new Exception($"提取CSB文件时出错:{ex.Message}", ex);
            }
        }
    }
}