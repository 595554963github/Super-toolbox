namespace super_toolbox
{
    public class BmpExtractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] START_SEQUENCE = { 0x42, 0x4D };

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

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(file)}");

                    if (Path.GetExtension(file).Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(file).Equals(".dib", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string sourceDirectory = Path.GetDirectoryName(file) ?? string.Empty;

                    if (string.IsNullOrEmpty(sourceDirectory))
                    {
                        ExtractionError?.Invoke(this, $"无法获取文件{file}的目录路径");
                        continue;
                    }

                    await ProcessFileAsync(file, sourceDirectory, Path.GetFileNameWithoutExtension(file), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    ExtractionError?.Invoke(this, $"处理文件{file}时出错:{ex.Message}");
                    OnExtractionFailed($"处理文件{file}时出错:{ex.Message}");
                }
            }

            ExtractionProgress?.Invoke(this, $"提取完成:提取了{ExtractedFileCount}个BMP文件");
            OnExtractionCompleted();
        }

        private async Task ProcessFileAsync(string filePath, string destinationFolder, string filePrefix, CancellationToken cancellationToken)
        {
            const int BufferSize = 8192;
            var startSequenceLength = START_SEQUENCE.Length;

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
            byte[] buffer = new byte[BufferSize];
            byte[] leftover = Array.Empty<byte>();
            MemoryStream? currentBmp = null;
            bool foundStart = false;
            int bmpCount = 0;

            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, BufferSize, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] currentData;
                if (leftover.Length > 0)
                {
                    currentData = new byte[leftover.Length + bytesRead];
                    Array.Copy(leftover, 0, currentData, 0, leftover.Length);
                    Array.Copy(buffer, 0, currentData, leftover.Length, bytesRead);
                }
                else
                {
                    currentData = new byte[bytesRead];
                    Array.Copy(buffer, 0, currentData, 0, bytesRead);
                }

                if (!foundStart)
                {
                    int startIndex = IndexOf(currentData, START_SEQUENCE);
                    if (startIndex != -1)
                    {
                        foundStart = true;
                        currentBmp = new MemoryStream();
                        currentBmp.Write(currentData, startIndex, currentData.Length - startIndex);
                        leftover = Array.Empty<byte>();
                    }
                    else
                    {
                        leftover = currentData.Length > startSequenceLength
                            ? currentData[^(startSequenceLength - 1)..]
                            : currentData;
                    }
                }
                else
                {
                    currentBmp!.Write(currentData, 0, currentData.Length);
                    byte[] bmpBytes = currentBmp.ToArray();

                    if (bmpBytes.Length >= 6)
                    {
                        uint fileSize = BitConverter.ToUInt32(bmpBytes, 2);

                        if (bmpBytes.Length >= fileSize)
                        {
                            if (ValidateBmpStructure(bmpBytes, (int)fileSize))
                            {
                                byte[] extractedData = new byte[fileSize];
                                Array.Copy(bmpBytes, 0, extractedData, 0, fileSize);

                                SaveBmpFile(extractedData, destinationFolder, filePrefix, bmpCount);
                                bmpCount++;

                                foundStart = false;
                                currentBmp.Dispose();
                                currentBmp = null;

                                if (fileSize < bmpBytes.Length)
                                {
                                    leftover = bmpBytes[(int)fileSize..];
                                }
                                else
                                {
                                    leftover = Array.Empty<byte>();
                                }
                            }
                        }
                    }
                    else
                    {
                        leftover = Array.Empty<byte>();
                    }
                }
            }

            currentBmp?.Dispose();
        }

        private bool ValidateBmpStructure(byte[] data, int expectedSize)
        {
            if (data.Length < 14) return false;

            if (data[0] != 0x42 || data[1] != 0x4D) return false;

            if (data.Length >= 18)
            {
                uint dibHeaderSize = BitConverter.ToUInt32(data, 14);

                if (dibHeaderSize != 12 && dibHeaderSize != 40 && dibHeaderSize != 56 &&
                    dibHeaderSize != 108 && dibHeaderSize != 124)
                {
                    if (dibHeaderSize < 12 || dibHeaderSize > 124) return false;
                }

                uint fileSize = BitConverter.ToUInt32(data, 2);
                if (fileSize != expectedSize && fileSize != 0) return false;
            }

            return true;
        }

        private void SaveBmpFile(byte[] bmpData, string destinationFolder, string filePrefix, int index)
        {
            string newFileName = $"{filePrefix}_{index + 1}.bmp";
            string filePath = Path.Combine(destinationFolder, newFileName);

            try
            {
                File.WriteAllBytes(filePath, bmpData);
                OnFileExtracted(filePath);
                ExtractionProgress?.Invoke(this, $"已提取:{newFileName}");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"保存文件{newFileName}时出错:{ex.Message}");
                OnExtractionFailed($"保存文件{newFileName}时出错:{ex.Message}");
            }
        }

        private static int IndexOf(byte[] source, byte[] pattern)
        {
            for (int i = 0; i <= source.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}