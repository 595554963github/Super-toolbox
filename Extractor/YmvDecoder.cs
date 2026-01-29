namespace super_toolbox
{
    public class YmvDecoder : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] MAGIC_HEADER = HexStringToByteArray("EF C9 ED F3 14 05 5C 51 51 5F");
        private static readonly byte[] WMV_MAGIC = HexStringToByteArray("30 26 B2 75");
        private static readonly byte[] JPG_HEADER = HexStringToByteArray("FF D8 FF");

        private static byte[] HexStringToByteArray(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        private byte[] DecryptSegment(byte[] segment)
        {
            byte[] output = new byte[segment.Length];
            for (int n = 0; n < segment.Length; n++)
            {
                byte key = (byte)(((n & 0xF) + 0x10) & 0xFF);
                output[n] = (byte)(segment[n] ^ key);
            }
            return output;
        }

        private bool IsJpegHeader(byte[] data, int startIndex)
        {
            if (startIndex + 2 >= data.Length) return false;
            return data[startIndex] == 0xFF &&
                   data[startIndex + 1] == 0xD8 &&
                   data[startIndex + 2] == 0xFF;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnExtractionFailed($"错误目录{directoryPath}不存在");
                return;
            }

            var ymvFiles = Directory.GetFiles(directoryPath, "*.ymv");
            if (ymvFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.ymv文件");
                OnExtractionFailed("未找到.ymv文件");
                return;
            }

            TotalFilesToExtract = ymvFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{ymvFiles.Length}个ymv文件");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var ymvFilePath in ymvFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        ExtractionProgress?.Invoke(this, $"正在处理:{Path.GetFileName(ymvFilePath)}");

                        byte[] data;
                        try
                        {
                            data = File.ReadAllBytes(ymvFilePath);
                        }
                        catch (Exception e)
                        {
                            ExtractionError?.Invoke(this, $"[错误]读取文件失败:{e.Message}");
                            continue;
                        }

                        if (data.Length >= WMV_MAGIC.Length)
                        {
                            bool isWmv = true;
                            for (int i = 0; i < WMV_MAGIC.Length; i++)
                            {
                                if (data[i] != WMV_MAGIC[i])
                                {
                                    isWmv = false;
                                    break;
                                }
                            }

                            if (isWmv)
                            {
                                string baseName = Path.GetFileNameWithoutExtension(ymvFilePath);
                                string outputDir = Path.Combine(Path.GetDirectoryName(ymvFilePath) ?? directoryPath, baseName);
                                Directory.CreateDirectory(outputDir);
                                string outPath = Path.Combine(outputDir, $"{baseName}.wmv");

                                try
                                {
                                    File.WriteAllBytes(outPath, data);
                                    ExtractionProgress?.Invoke(this, $"[OK]检测到WMV文件,已输出到{outPath} ({data.Length}字节)");
                                    OnFileExtracted(outPath);
                                }
                                catch (Exception e)
                                {
                                    ExtractionError?.Invoke(this, $"[错误]写入WMV文件失败:{e.Message}");
                                }
                                continue;
                            }
                        }

                        string ymvFileDir = Path.GetDirectoryName(ymvFilePath) ?? directoryPath;
                        string ymvBaseName = Path.GetFileNameWithoutExtension(ymvFilePath);
                        string outputFolder = Path.Combine(ymvFileDir, ymvBaseName);
                        Directory.CreateDirectory(outputFolder);

                        List<int> segmentStarts = new List<int>();
                        for (int i = 0; i <= data.Length - MAGIC_HEADER.Length; i++)
                        {
                            bool found = true;
                            for (int j = 0; j < MAGIC_HEADER.Length; j++)
                            {
                                if (data[i + j] != MAGIC_HEADER[j])
                                {
                                    found = false;
                                    break;
                                }
                            }
                            if (found) segmentStarts.Add(i);
                        }

                        if (segmentStarts.Count == 0)
                        {
                            ExtractionError?.Invoke(this, "[错误]文件中未找到任何有效段。");
                            continue;
                        }

                        int segmentIndex = 1;
                        for (int i = 0; i < segmentStarts.Count; i++)
                        {
                            int segmentStart = segmentStarts[i];
                            int segmentEnd = (i < segmentStarts.Count - 1) ? segmentStarts[i + 1] : data.Length;
                            int segmentLength = segmentEnd - segmentStart;

                            byte[] rawSegment = new byte[segmentLength];
                            Array.Copy(data, segmentStart, rawSegment, 0, segmentLength);

                            byte[] decrypted = DecryptSegment(rawSegment);

                            for (int j = 0; j < decrypted.Length - 2; j++)
                            {
                                if (IsJpegHeader(decrypted, j))
                                {
                                    int jpegStart = j;
                                    int jpegEnd = decrypted.Length;

                                    for (int k = jpegStart + 2; k < decrypted.Length - 1; k++)
                                    {
                                        if (decrypted[k] == 0xFF && decrypted[k + 1] == 0xD9)
                                        {
                                            jpegEnd = k + 2;
                                            break;
                                        }
                                    }

                                    if (jpegEnd > jpegStart)
                                    {
                                        byte[] jpegData = new byte[jpegEnd - jpegStart];
                                        Array.Copy(decrypted, jpegStart, jpegData, 0, jpegData.Length);

                                        string outFile = Path.Combine(outputFolder, $"output_{segmentIndex:000}.jpg");
                                        try
                                        {
                                            File.WriteAllBytes(outFile, jpegData);
                                            ExtractionProgress?.Invoke(this, $"[OK]写出{outFile} ({jpegData.Length}字节)");
                                            OnFileExtracted(outFile);
                                            segmentIndex++;
                                        }
                                        catch (Exception e)
                                        {
                                            ExtractionError?.Invoke(this, $"[错误]写文件失败{outFile}: {e.Message}");
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"处理失败:{ex.Message}");
                OnExtractionFailed($"处理失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}