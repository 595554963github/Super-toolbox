namespace super_toolbox
{
    public class Yay0_Compressor : BaseExtractor
    {
        private static readonly byte[] Yay0Magic = { 0x59, 0x61, 0x79, 0x30 }; // "Yay0"
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
            var filesToCompress = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
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
                    foreach (var file in Directory.GetFiles(compressedDir, "*.yay0", SearchOption.AllDirectories))
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
                        string relativePath = GetRelativePath(directoryPath, filePath);
                        string outputPath = Path.Combine(compressedDir, relativePath + ".yay0");
                        string outputDir = Path.GetDirectoryName(outputPath) ??
                            throw new InvalidOperationException($"无法确定输出目录路径:{outputPath}");
                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }
                        try
                        {
                            byte[] inputData = File.ReadAllBytes(filePath);
                            byte[] compressedData = CompressYay0(inputData);
                            if (compressedData != null && compressedData.Length > 0)
                            {
                                File.WriteAllBytes(outputPath, compressedData);
                                CompressionProgress?.Invoke(this, $"已压缩:{Path.GetFileName(outputPath)} (原始大小: {inputData.Length} 压缩后: {compressedData.Length})");
                                OnFileCompressed(outputPath);
                            }
                            else
                            {
                                CompressionError?.Invoke(this, $"压缩失败: 输出数据为空 - {filePath}");
                                OnCompressionFailed($"压缩失败: 输出数据为空 - {filePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            CompressionError?.Invoke(this, $"压缩文件{filePath}时出错:{ex.Message}");
                            OnCompressionFailed($"压缩文件{filePath}时出错:{ex.Message}");
                        }
                    }
                    OnCompressionCompleted();
                    CompressionProgress?.Invoke(this, $"压缩完成，共压缩{TotalFilesToCompress}个文件");
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
        private byte[] CompressYay0(byte[] input)
        {
            if (input == null || input.Length == 0)
                return new byte[0];
            int insize = input.Length;
            List<uint> cmd = new List<uint>();
            List<ushort> pol = new List<ushort>();
            List<byte> def = new List<byte>();
            int dp = 0; 
            int pp = 0; 
            int cp = 0; 
            uint currentCmd = 0;
            uint cmdMask = 0x80000000;
            int position = 0;
            while (position < insize)
            {
                int bestMatchOffset = 0;
                uint bestMatchLength = 0;
                Search(input, position, insize, out bestMatchOffset, out bestMatchLength);
                if (bestMatchLength <= 2)
                {
                    currentCmd |= cmdMask;
                    def.Add(input[position]);
                    dp++;
                    position++;
                }
                else
                {
                    int nextMatchOffset;
                    uint nextMatchLength;
                    Search(input, position + 1, insize, out nextMatchOffset, out nextMatchLength);
                    if (nextMatchLength > bestMatchLength + 1)
                    {
                        currentCmd |= cmdMask;
                        def.Add(input[position]);
                        dp++;
                        position++;
                        cmdMask >>= 1;
                        if (cmdMask == 0)
                        {
                            cmd.Add(currentCmd);
                            cp++;
                            currentCmd = 0;
                            cmdMask = 0x80000000;
                        }
                        bestMatchLength = nextMatchLength;
                        bestMatchOffset = nextMatchOffset;
                    }
                    int offset = position - bestMatchOffset - 1;
                    if (bestMatchLength > 0x11)
                    {
                        pol.Add((ushort)offset);
                        def.Add((byte)(bestMatchLength - 18));
                        dp++;
                    }
                    else
                    {
                        pol.Add((ushort)(offset | (((ushort)bestMatchLength - 2) << 12)));
                    }
                    pp++;
                    position += (int)bestMatchLength;
                }
                cmdMask >>= 1;
                if (cmdMask == 0)
                {
                    cmd.Add(currentCmd);
                    cp++;
                    currentCmd = 0;
                    cmdMask = 0x80000000;
                }
            }
            if (cmdMask != 0x80000000)
            {
                cmd.Add(currentCmd);
                cp++;
            }
            List<byte> output = new List<byte>();
            output.AddRange(Yay0Magic);
            output.AddRange(BitConverter.GetBytes(insize).Reverse());
            int linkTableOffset = 16 + cmd.Count * 4;
            int chunkOffset = linkTableOffset + pol.Count * 2;
            output.AddRange(BitConverter.GetBytes(linkTableOffset).Reverse());
            output.AddRange(BitConverter.GetBytes(chunkOffset).Reverse());
            foreach (uint cmdValue in cmd)
            {
                output.AddRange(BitConverter.GetBytes(cmdValue).Reverse());
            }
            foreach (ushort polValue in pol)
            {
                output.AddRange(BitConverter.GetBytes(polValue).Reverse());
            }
            output.AddRange(def);
            return output.ToArray();
        }
        private void Search(byte[] input, int position, int insize, out int bestOffset, out uint bestLength)
        {
            bestOffset = 0;
            bestLength = 0;
            if (position >= insize)
                return;
            int searchStart = Math.Max(0, position - 4096);
            int maxLength = Math.Min(273, insize - position);
            if (maxLength <= 2)
                return;
            for (int i = searchStart; i < position; i++)
            {
                int matchLength = 0;
                while (matchLength < maxLength &&
                       i + matchLength < position &&
                       position + matchLength < insize &&
                       input[i + matchLength] == input[position + matchLength])
                {
                    matchLength++;
                }
                if (matchLength > bestLength)
                {
                    bestLength = (uint)matchLength;
                    bestOffset = i;
                }
            }
        }
        private string GetRelativePath(string rootPath, string fullPath)
        {
            Uri rootUri = new Uri(rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }
        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}