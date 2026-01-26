namespace super_toolbox
{
    public class Lzma_Compressor : BaseExtractor
    {
        public new event EventHandler<string>? CompressionStarted;
        public new event EventHandler<string>? CompressionProgress;
        public new event EventHandler<string>? CompressionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                CompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnCompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            var filesToCompress = Directory.GetFiles(directoryPath, "*.*");
            if (filesToCompress.Length == 0)
            {
                CompressionError?.Invoke(this, "未找到需要压缩的文件");
                OnCompressionFailed("未找到需要压缩的文件");
                return;
            }

            string compressedDir = Path.Combine(directoryPath, "Compressed");
            Directory.CreateDirectory(compressedDir);
            CompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(compressedDir, "*.lzma"))
                    {
                        File.Delete(file);
                    }

                    TotalFilesToCompress = filesToCompress.Length;
                    int processedFiles = 0;

                    foreach (var filePath in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;
                        CompressionProgress?.Invoke(this, $"正在压缩文件({processedFiles}/{TotalFilesToCompress}): {Path.GetFileName(filePath)}");

                        string fileName = Path.GetFileName(filePath);
                        string outputPath = Path.Combine(compressedDir, fileName + ".lzma");

                        try
                        {
                            CompressFileWithLzma(filePath, outputPath);

                            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                            {
                                CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)}");
                                OnFileCompressed(outputPath);
                            }
                            else
                            {
                                CompressionError?.Invoke(this, $"压缩成功但输出文件异常:{outputPath}");
                                OnCompressionFailed($"压缩成功但输出文件异常:{outputPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            CompressionError?.Invoke(this, $"压缩文件{filePath}时出错:{ex.Message}");
                            OnCompressionFailed($"压缩文件{filePath}时出错:{ex.Message}");
                        }
                    }

                    OnCompressionCompleted();
                    CompressionProgress?.Invoke(this, $"压缩完成,共压缩{TotalFilesToCompress}个文件");
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CompressionError?.Invoke(this, "压缩操作已取消");
                OnCompressionFailed("压缩操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                CompressionError?.Invoke(this, $"压缩过程出错:{ex.Message}");
                OnCompressionFailed($"压缩过程出错:{ex.Message}");
            }
        }

        private void CompressFileWithLzma(string inputPath, string outputPath)
        {
            byte[] inputData = File.ReadAllBytes(inputPath);
            byte[] compressedData = LzmaHelper.Compress(inputData);
            File.WriteAllBytes(outputPath, compressedData);
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }

    public static class LzmaHelper
    {
        public static byte[] Compress(byte[] input)
        {
            var encoder = new SevenZip.Compression.LZMA.Encoder();
            using var inputStream = new MemoryStream(input);
            using var outputStream = new MemoryStream();

            encoder.WriteCoderProperties(outputStream);
            outputStream.Write(BitConverter.GetBytes(inputStream.Length), 0, 8);

            encoder.Code(inputStream, outputStream, inputStream.Length, -1, null);
            return outputStream.ToArray();
        }

        public static byte[] Decompress(byte[] input)
        {
            var decoder = new SevenZip.Compression.LZMA.Decoder();
            using var inputStream = new MemoryStream(input);
            using var outputStream = new MemoryStream();

            byte[] properties = new byte[5];
            inputStream.Read(properties, 0, 5);
            byte[] lengthBytes = new byte[8];
            inputStream.Read(lengthBytes, 0, 8);
            long fileLength = BitConverter.ToInt64(lengthBytes, 0);

            decoder.SetDecoderProperties(properties);
            decoder.Code(inputStream, outputStream, inputStream.Length, fileLength, null);

            return outputStream.ToArray();
        }
    }
}
