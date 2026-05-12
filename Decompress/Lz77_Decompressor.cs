namespace super_toolbox
{
    public class Lz77_Decompressor : BaseExtractor
    {
        public event EventHandler<string>? DecompressionStarted;
        public event EventHandler<string>? DecompressionProgress;
        public event EventHandler<string>? DecompressionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                DecompressionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnDecompressionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            string decompressedDir = Path.Combine(directoryPath, "Decompressed");
            Directory.CreateDirectory(decompressedDir);
            DecompressionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            try
            {
                await Task.Run(() =>
                {
                    var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
                    var filesToProcess = allFiles.Where(IsLz77File).ToArray();

                    if (filesToProcess.Length == 0)
                    {
                        DecompressionError?.Invoke(this, "未找到有效的LZ77压缩文件");
                        OnDecompressionFailed("未找到有效的LZ77压缩文件");
                        return;
                    }

                    TotalFilesToDecompress = filesToProcess.Length;
                    int processedFiles = 0;

                    foreach (var filePath in filesToProcess)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedFiles++;
                        DecompressionProgress?.Invoke(this, $"正在解压文件({processedFiles}/{TotalFilesToDecompress}): {Path.GetFileName(filePath)}");

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputPath = Path.Combine(decompressedDir, fileName);
                        string outputDir = Path.GetDirectoryName(outputPath) ??
                            throw new InvalidOperationException($"无法确定输出目录路径:{outputPath}");

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        try
                        {
                            if (DecompressLz77File(filePath, outputPath))
                            {
                                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                                {
                                    DecompressionProgress?.Invoke(this, $"已解压:{Path.GetFileName(outputPath)}");
                                    OnFileDecompressed(outputPath);
                                }
                                else
                                {
                                    DecompressionError?.Invoke(this, $"解压成功但输出文件异常:{outputPath}");
                                    OnDecompressionFailed($"解压成功但输出文件异常:{outputPath}");
                                }
                            }
                            else
                            {
                                DecompressionError?.Invoke(this, $"解压文件失败:{filePath}");
                                OnDecompressionFailed($"解压文件失败:{filePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DecompressionError?.Invoke(this, $"解压文件{filePath}时出错:{ex.Message}");
                            OnDecompressionFailed($"解压文件{filePath}时出错:{ex.Message}");
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
                DecompressionError?.Invoke(this, $"解压过程出错:{ex.Message}");
                OnDecompressionFailed($"解压过程出错:{ex.Message}");
            }
        }

        private bool IsLz77File(string filePath)
        {
            try
            {
                return Path.GetExtension(filePath).Equals(".lz77", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool DecompressLz77File(string inputPath, string outputPath)
        {
            try
            {
                byte[] input = File.ReadAllBytes(inputPath);
                List<byte> output = new List<byte>();
                int position = 0;

                while (position < input.Length)
                {
                    if (position + 1 < input.Length && input[position] == 0)
                    {
                        output.Add(input[position + 1]);
                        position += 2;
                    }
                    else if (position + 3 <= input.Length)
                    {
                        ushort distance = BitConverter.ToUInt16(input, position);
                        byte length = input[position + 2];

                        int start = output.Count - distance;
                        for (int i = 0; i < length; i++)
                        {
                            if (start + i < output.Count)
                                output.Add(output[start + i]);
                        }

                        position += 3;
                    }
                    else
                    {
                        break;
                    }
                }

                File.WriteAllBytes(outputPath, output.ToArray());
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}
