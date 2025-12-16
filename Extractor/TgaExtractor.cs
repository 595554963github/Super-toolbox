using System.Buffers;

namespace super_toolbox
{
    public class TgaExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;
        public int MaxDimension { get; set; } = 8192;
        public HashSet<byte> SupportedImageTypes { get; } = new HashSet<byte> { 1, 2, 3, 9, 10, 11 };
        public HashSet<byte> SupportedBitDepths { get; } = new HashSet<byte> { 8, 16, 24, 32, 15, 2 };

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

            var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            TotalFilesToExtract = files.Length;

            ExtractionStarted?.Invoke(this, $"开始处理{TotalFilesToExtract}个文件");

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            try
            {
                await Parallel.ForEachAsync(files, parallelOptions, async (file, token) =>
                {
                    try
                    {
                        ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(file)}");

                        if (Path.GetExtension(file).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        string fileDir = Path.GetDirectoryName(file) ?? directoryPath;
                        await ProcessFileAsync(file, fileDir, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{file}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{file} 时出错:{ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }

            ExtractionProgress?.Invoke(this, $"提取完成:提取了{ExtractedFileCount}个tga图像");
            OnExtractionCompleted();
        }

        private async Task ProcessFileAsync(string filePath, string destinationFolder, CancellationToken cancellationToken)
        {
            const int BufferSize = 8192;
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            byte[] leftover = Array.Empty<byte>();
            MemoryStream? currentTga = null;
            bool foundStart = false;
            string filePrefix = Path.GetFileNameWithoutExtension(filePath);
            int tgaCount = 0;

            try
            {
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, BufferSize, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[] combinedData;
                    if (leftover.Length == 0)
                    {
                        combinedData = new byte[bytesRead];
                        Array.Copy(buffer, 0, combinedData, 0, bytesRead);
                    }
                    else
                    {
                        combinedData = new byte[leftover.Length + bytesRead];
                        Array.Copy(leftover, 0, combinedData, 0, leftover.Length);
                        Array.Copy(buffer, 0, combinedData, leftover.Length, bytesRead);
                    }

                    leftover = Array.Empty<byte>();

                    if (!foundStart)
                    {
                        int startIndex = FindTgaStart(combinedData);
                        if (startIndex != -1)
                        {
                            foundStart = true;
                            currentTga = new MemoryStream();
                            currentTga.Write(combinedData, startIndex, combinedData.Length - startIndex);
                        }
                        else
                        {
                            int tgaSignatureLength = 18;
                            leftover = combinedData.Length > tgaSignatureLength - 1
                                ? combinedData[^(tgaSignatureLength - 1)..]
                                : combinedData;
                        }
                    }
                    else
                    {
                        currentTga!.Write(combinedData, 0, combinedData.Length);
                        byte[] tgaBytes = currentTga.ToArray();

                        int tgaSize = CalculateTgaSize(tgaBytes);
                        if (tgaSize > 0 && tgaSize <= tgaBytes.Length)
                        {
                            byte[] extractedData = new byte[tgaSize];
                            Array.Copy(tgaBytes, 0, extractedData, 0, tgaSize);

                            if (ValidateTga(extractedData))
                            {
                                SaveTgaFile(extractedData, destinationFolder, filePrefix, tgaCount);
                                tgaCount++;
                            }

                            foundStart = false;
                            currentTga.Dispose();
                            currentTga = null;

                            leftover = tgaSize < tgaBytes.Length ? tgaBytes[tgaSize..] : Array.Empty<byte>();
                        }
                    }
                }
            }
            finally
            {
                currentTga?.Dispose();
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private int FindTgaStart(byte[] data)
        {
            for (int i = 0; i <= data.Length - 18; i++)
            {
                if (IsValidTgaHeader(data, i))
                {
                    return i;
                }
            }
            return -1;
        }

        private int CalculateTgaSize(byte[] data)
        {
            if (data.Length < 18) return -1;

            try
            {
                if (!IsValidTgaHeader(data, 0))
                    return -1;

                byte idLength = data[0];
                byte colorMapType = data[1];

                ushort colorMapLength = BitConverter.ToUInt16(data, 5);
                byte colorMapEntrySize = data[7];
                ushort width = BitConverter.ToUInt16(data, 12);
                ushort height = BitConverter.ToUInt16(data, 14);
                byte bitsPerPixel = data[16];

                int pixelSize = bitsPerPixel / 8;
                if (pixelSize == 0) return -1;

                int totalSize = 18 + idLength;

                if (colorMapType == 1)
                {
                    int colorMapPixelSize = colorMapEntrySize / 8;
                    if (colorMapPixelSize == 0) return -1;
                    totalSize += colorMapLength * colorMapPixelSize;
                }

                totalSize += width * height * pixelSize;

                return totalSize <= data.Length && totalSize >= 18 + idLength ? totalSize : -1;
            }
            catch
            {
                return -1;
            }
        }

        private bool ValidateTga(byte[] data)
        {
            if (data.Length < 18) return false;

            try
            {
                if (!IsValidTgaHeader(data, 0))
                    return false;

                byte idLength = data[0];
                byte colorMapType = data[1];
                ushort width = BitConverter.ToUInt16(data, 12);
                ushort height = BitConverter.ToUInt16(data, 14);
                byte bitsPerPixel = data[16];

                int pixelSize = bitsPerPixel / 8;
                if (pixelSize == 0) return false;

                int expectedSize = 18 + idLength;

                if (colorMapType == 1)
                {
                    ushort colorMapLength = BitConverter.ToUInt16(data, 5);
                    byte colorMapEntrySize = data[7];
                    int colorMapPixelSize = colorMapEntrySize / 8;
                    if (colorMapPixelSize == 0) return false;
                    expectedSize += colorMapLength * colorMapPixelSize;
                }

                expectedSize += width * height * pixelSize;

                return data.Length == expectedSize;
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidTgaHeader(byte[] data, int startIndex)
        {
            if (data.Length - startIndex < 18)
                return false;

            byte imageType = data[startIndex + 2];
            byte colorMapType = data[startIndex + 1];
            byte bitsPerPixel = data[startIndex + 16];
            ushort width = BitConverter.ToUInt16(data, startIndex + 12);
            ushort height = BitConverter.ToUInt16(data, startIndex + 14);

            if (!SupportedImageTypes.Contains(imageType))
                return false;

            if (colorMapType > 1)
                return false;

            if (!SupportedBitDepths.Contains(bitsPerPixel))
                return false;

            if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension)
                return false;

            return true;
        }

        private void SaveTgaFile(byte[] tgaData, string destinationFolder, string filePrefix, int index)
        {
            string newFileName = $"{filePrefix}_{index + 1}.tga";
            string filePath = Path.Combine(destinationFolder, newFileName);

            try
            {
                File.WriteAllBytes(filePath, tgaData);
                OnFileExtracted(filePath);
                ExtractionProgress?.Invoke(this, $"已提取:{newFileName}");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"保存文件{newFileName}时出错:{ex.Message}");
                OnExtractionFailed($"保存文件{newFileName}时出错:{ex.Message}");
            }
        }
    }
}