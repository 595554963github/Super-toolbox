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
            List<string> extractedFiles = new List<string>();

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
            int actualFileCount = 0;
            ExtractionStarted?.Invoke(this, $"正在分析{allFiles.Length}个文件，统计可提取内容...");

            foreach (var filePath in allFiles)
            {
                try
                {
                    actualFileCount += await CountExtractableFilesAsync(filePath, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"分析文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
                }
            }
            TotalFilesToExtract = actualFileCount;
            ExtractionStarted?.Invoke(this, $"开始处理{allFiles.Length}个文件(.elixir/.gz)，预计提取{actualFileCount}个文件(g1m/g1t)");

            int extractedCount = 0;

            foreach (var filePath in allFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(filePath)}");

                try
                {
                    extractedCount += await ExtractFileAsync(filePath, directoryPath, extractedFiles, cancellationToken);
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

            ExtractionProgress?.Invoke(this, $"提取完成，共提取{extractedCount}个文件(g1m/g1t)");
            OnExtractionCompleted();
        }
        private async Task<int> CountExtractableFilesAsync(string filePath, CancellationToken cancellationToken)
        {
            int count = 0;

            try
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                if (filePath.EndsWith(".elixir.gz", StringComparison.OrdinalIgnoreCase))
                {
                    fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileNameWithoutExt);
                }

                using var inputStream = File.OpenRead(filePath);
                byte[] marker = new byte[] { 0x00, 0x00, 0x78, 0x9C };
                byte[] buffer = new byte[4];
                long position = 0;
                bool hasData = false;
                using var decompressedStream = new MemoryStream();

                while (position + 4 < inputStream.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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
                            await decompressor.CopyToAsync(decompressedStream, cancellationToken);
                            hasData = true;
                        }
                    }
                    position++;
                }

                if (hasData)
                {
                    byte[] decompressedData = decompressedStream.ToArray();

                    byte[] g1mMarker = new byte[] { 0x5F, 0x4D, 0x31, 0x47 };
                    byte[] g1tMarker = new byte[] { 0x47, 0x54, 0x31, 0x47 };

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
                                    count++;
                                    i += size - 1; 
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            return count;
        }
        private async Task<int> ExtractFileAsync(string filePath, string baseDirectory, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            int fileCount = 0;

            try
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                if (filePath.EndsWith(".elixir.gz", StringComparison.OrdinalIgnoreCase))
                {
                    fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileNameWithoutExt);
                }

                string outputDir = Path.Combine(Path.GetDirectoryName(filePath) ?? baseDirectory, fileNameWithoutExt);
                Directory.CreateDirectory(outputDir);

                using var inputStream = File.OpenRead(filePath);
                byte[] marker = new byte[] { 0x00, 0x00, 0x78, 0x9C };
                byte[] buffer = new byte[4];
                long position = 0;
                bool hasData = false;

                using var decompressedStream = new MemoryStream();

                while (position + 4 < inputStream.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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
                            await decompressor.CopyToAsync(decompressedStream, cancellationToken);
                            hasData = true;
                        }
                    }
                    position++;
                }

                if (hasData)
                {
                    byte[] decompressedData = decompressedStream.ToArray();

                    byte[] g1mMarker = new byte[] { 0x5F, 0x4D, 0x31, 0x47 };
                    byte[] g1tMarker = new byte[] { 0x47, 0x54, 0x31, 0x47 };

                    int localFileCount = 0;

                    for (int i = 0; i <= decompressedData.Length - 4; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

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
                                    string savePath = Path.Combine(outputDir, $"{fileNameWithoutExt}_{localFileCount}{extension}");

                                    using (var fs = File.Create(savePath))
                                    {
                                        fs.Write(decompressedData, i, size);
                                    }
                                    OnFileExtracted(savePath);
                                    ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(savePath)}");

                                    extractedFiles.Add(savePath);
                                    localFileCount++;
                                    fileCount++;
                                }
                            }
                        }
                    }

                    if (localFileCount == 0)
                    {
                        Directory.Delete(outputDir, true);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取文件{Path.GetFileName(filePath)}时出错:{ex.Message}");
            }

            return fileCount;
        }
    }
}
