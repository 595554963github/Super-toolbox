using System.Text;

namespace super_toolbox
{
    public class Wflz_Decompressor : BaseExtractor
    {
        public new event EventHandler<string>? DecompressionStarted;
        public new event EventHandler<string>? DecompressionProgress;
        public new event EventHandler<string>? DecompressionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
                    var filesToProcess = allFiles.Where(IsWflzFile).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的WFLZ压缩文件");
                        OnDecompressionFailed("未找到有效的WFLZ压缩文件");
                        return;
                    }

                    string decompressedDir = Path.Combine(directoryPath, "Decompressed");
                    Directory.CreateDirectory(decompressedDir);

                    TotalFilesToDecompress = filesToProcess.Length;
                    DecompressionStarted?.Invoke(this, $"开始解压,共{TotalFilesToDecompress}个文件");

                    int processedFiles = 0;
                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;

                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        if (DecompressWflzFile(filePath, decompressedDir))
                        {
                            string originalFileName = Path.GetFileNameWithoutExtension(filePath);
                            string outputPath = Path.Combine(decompressedDir, originalFileName);

                            OnFileDecompressed(outputPath);
                            DecompressionProgress?.Invoke(this, $"已解压:{originalFileName}");
                        }
                        else
                        {
                            DecompressionError?.Invoke(this, $"解压失败:{Path.GetFileName(filePath)}");
                            OnDecompressionFailed($"解压失败:{Path.GetFileName(filePath)}");
                        }
                    }

                    OnDecompressionCompleted();
                    DecompressionProgress?.Invoke(this, $"解压完成,共解压{TotalFilesToDecompress}个文件");
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                DecompressionError?.Invoke(this, "解压操作已取消");
                OnDecompressionFailed("解压操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"解压失败:{ex.Message}");
                OnDecompressionFailed($"解压失败:{ex.Message}");
            }
        }

        private bool IsWflzFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension == ".wflz";
        }

        private bool DecompressWflzFile(string inputPath, string outputDir)
        {
            try
            {
                byte[] compressedData = File.ReadAllBytes(inputPath);

                if (compressedData.Length < 16)
                {
                    DecompressionError?.Invoke(this, "文件大小不足");
                    return false;
                }

                string signature = Encoding.ASCII.GetString(compressedData, 0, 4);
                if (signature != "WFLZ" && signature != "ZLF")
                {
                    DecompressionError?.Invoke(this, "无效的WFLZ文件签名");
                    return false;
                }

                uint decompressedSize = BitConverter.ToUInt32(compressedData, 8);
                byte[] decompressedData = new byte[decompressedSize];

                wfLZ_Decompress(compressedData, decompressedData);

                string originalFileName = Path.GetFileNameWithoutExtension(inputPath);
                string outputPath = Path.Combine(outputDir, originalFileName);

                string? outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.WriteAllBytes(outputPath, decompressedData);
                return true;
            }
            catch (Exception ex)
            {
                DecompressionError?.Invoke(this, $"WFLZ解压错误:{ex.Message}");
                return false;
            }
        }

        private const int WFLZ_BLOCK_SIZE = 4;
        private const int WFLZ_MIN_MATCH_LEN = WFLZ_BLOCK_SIZE + 1;

        private struct wfLZ_Block
        {
            public ushort dist;
            public byte length;
            public byte numLiterals;
        }

        private void wfLZ_Decompress(byte[] input, byte[] output)
        {
            int srcIndex = 16;
            int dstIndex = 0;
            byte numLiterals = input[15];
            wfLZ_Block block;

        WF_LZ_LITERALS:
            output[dstIndex++] = input[srcIndex++];
            numLiterals--;
            if (numLiterals > 0) goto WF_LZ_LITERALS;

            WF_LZ_BLOCK:
            block.dist = BitConverter.ToUInt16(input, srcIndex);
            block.length = input[srcIndex + 2];
            block.numLiterals = input[srcIndex + 3];
            numLiterals = block.numLiterals;

            ushort dist = block.dist;
            ushort len = block.length;

            if (len != 0)
            {
                len += (ushort)(WFLZ_MIN_MATCH_LEN - 1);
                int copySrc = dstIndex - dist;
                for (int i = 0; i < len; i++)
                {
                    output[dstIndex + i] = output[copySrc + i];
                }
                dstIndex += len;
            }
            srcIndex += WFLZ_BLOCK_SIZE;

            if (numLiterals == 0)
            {
                if (dist == 0 && len == 0)
                {
                    return;
                }
                goto WF_LZ_BLOCK;
            }
            else
            {
                goto WF_LZ_LITERALS;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
