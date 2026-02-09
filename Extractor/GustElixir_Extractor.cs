using System.IO.Compression;

namespace super_toolbox
{
    public class GustElixir_Extractor : BaseExtractor
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
                ExtractionError?.Invoke(this, $"错误:{directoryPath}不是有效的目录");
                OnExtractionFailed($"错误:{directoryPath}不是有效的目录");
                return;
            }

            var elixirFiles = Directory.EnumerateFiles(directoryPath, "*.elixir", SearchOption.AllDirectories);
            var elixirGzFiles = Directory.EnumerateFiles(directoryPath, "*.elixir.gz", SearchOption.AllDirectories);
            var gzFiles = Directory.EnumerateFiles(directoryPath, "*.gz", SearchOption.AllDirectories)
                .Where(file => !file.EndsWith(".elixir.gz", StringComparison.OrdinalIgnoreCase));

            var allFiles = elixirFiles.Concat(elixirGzFiles).Concat(gzFiles).Distinct().ToArray();

            if (allFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.elixir或.gz文件");
                OnExtractionFailed("未找到.elixir或.gz文件");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理{allFiles.Length}个文件(.elixir/.gz)");

            int totalExtractedFiles = 0;

            foreach (var filePath in allFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");

                try
                {
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                    if (filePath.EndsWith(".elixir.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileNameWithoutExt);
                    }

                    string outputDir = Path.Combine(Path.GetDirectoryName(filePath) ?? directoryPath, fileNameWithoutExt);
                    Directory.CreateDirectory(outputDir);

                    string outputFilePath = Path.Combine(outputDir, fileNameWithoutExt + ".decompress");

                    bool hasData = false;

                    using (var inputStream = File.OpenRead(filePath))
                    using (var outputStream = File.Create(outputFilePath))
                    {
                        byte[] marker = new byte[] { 0x00, 0x00, 0x78, 0x9C };
                        byte[] buffer = new byte[4];
                        long position = 0;

                        while (position + 4 < inputStream.Length)
                        {
                            inputStream.Position = position;
                            inputStream.Read(buffer, 0, 4);

                            if (buffer.SequenceEqual(marker))
                            {
                                inputStream.Position = position - 2;
                                byte[] sizeBytes = new byte[2];
                                inputStream.Read(sizeBytes, 0, 2);
                                int compressedSize = BitConverter.ToUInt16(sizeBytes, 0);

                                inputStream.Position = position + 4;
                                byte[] compressedBlock = new byte[compressedSize];
                                inputStream.Read(compressedBlock, 0, compressedSize);

                                using (var compressedStream = new MemoryStream(compressedBlock))
                                using (var decompressor = new DeflateStream(compressedStream, CompressionMode.Decompress))
                                {
                                    decompressor.CopyTo(outputStream);
                                    hasData = true;
                                }
                            }
                            position++;
                        }
                    }

                    if (hasData)
                    {
                        byte[] decompressedData = File.ReadAllBytes(outputFilePath);
                        File.Delete(outputFilePath);

                        byte[] g1mMarker = new byte[] { 0x5F, 0x4D, 0x31, 0x47 };
                        byte[] g1tMarker = new byte[] { 0x47, 0x54, 0x31, 0x47 };

                        int fileCount = 0;

                        for (int i = 0; i <= decompressedData.Length - 4; i++)
                        {
                            bool isG1m = decompressedData[i] == g1mMarker[0] &&
                                         decompressedData[i + 1] == g1mMarker[1] &&
                                         decompressedData[i + 2] == g1mMarker[2] &&
                                         decompressedData[i + 3] == g1mMarker[3];

                            bool isG1t = decompressedData[i] == g1tMarker[0] &&
                                         decompressedData[i + 1] == g1tMarker[1] &&
                                         decompressedData[i + 2] == g1tMarker[2] &&
                                         decompressedData[i + 3] == g1tMarker[3];

                            if (isG1m || isG1t)
                            {
                                if (i + 0x0B < decompressedData.Length)
                                {
                                    int size = BitConverter.ToInt32(decompressedData, i + 0x08);
                                    if (size > 0 && i + size <= decompressedData.Length)
                                    {
                                        string extension = isG1m ? ".g1m" : ".g1t";
                                        string savePath = Path.Combine(outputDir, $"{fileNameWithoutExt}_{fileCount}{extension}");
                                        using (var fs = File.Create(savePath))
                                        {
                                            fs.Write(decompressedData, i, size);
                                        }
                                        fileCount++;
                                        OnFileExtracted(savePath);
                                        totalExtractedFiles++;
                                    }
                                }
                            }
                        }

                        if (fileCount == 0)
                        {
                            Directory.Delete(outputDir, true);
                        }
                    }
                    else
                    {
                        File.Delete(outputFilePath);
                        Directory.Delete(outputDir, true);
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"文件{Path.GetFileName(filePath)}处理错误:{ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"提取完成，总共提取{totalExtractedFiles}个文件");
            OnExtractionCompleted();
        }
    }
}
