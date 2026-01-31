using System.Text;

namespace super_toolbox
{
    public class AfsRepacker : BaseExtractor
    {      
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;
        private static readonly byte[] AFS_MAGIC = { 0x41, 0x46, 0x53, 0x00 };
        private const uint AFS_DATA_START = 0x80000;
        private const uint ALIGNMENT = 0x800;
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await RepackAsync(directoryPath, cancellationToken);
        }

        public async Task RepackAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, "错误:目录不存在");
                OnPackingFailed("错误:目录不存在");
                return;
            }

            string parentDir = Directory.GetParent(directoryPath)?.FullName ?? directoryPath;
            string folderName = Path.GetFileName(directoryPath);
            string outputAfsPath = Path.Combine(parentDir, folderName + ".afs");

            var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                                   .Where(f => !f.EndsWith(".afs", StringComparison.OrdinalIgnoreCase))
                                   .ToArray();

            TotalFilesToPack = allFiles.Length;

            PackingStarted?.Invoke(this, $"开始打包目录:{directoryPath}");
            PackingProgress?.Invoke(this, $"找到{allFiles.Length}个文件:");

            foreach (var file in allFiles)
            {
                string relativePath = GetRelativePath(directoryPath, file);
                PackingProgress?.Invoke(this, $"{relativePath}");
            }

            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PackingProgress?.Invoke(this, $"正在打包AFS文件:{Path.GetFileName(outputAfsPath)}");

                    using (var fs = new FileStream(outputAfsPath, FileMode.Create, FileAccess.Write))
                    using (var writer = new BinaryWriter(fs, Encoding.UTF8))
                    {
                        fs.Seek(AFS_DATA_START, SeekOrigin.Begin);

                        List<AfsFileHeader> headers = new List<AfsFileHeader>();
                        uint currentOffset = AFS_DATA_START;

                        foreach (var filePath in allFiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                            {
                                AfsFileHeader header = new AfsFileHeader
                                {
                                    Offset = currentOffset,
                                    Size = (uint)fileStream.Length
                                };

                                fileStream.CopyTo(fs);
                                currentOffset += header.Size;

                                uint padding = ALIGNMENT - (currentOffset % ALIGNMENT);
                                if (padding < ALIGNMENT)
                                {
                                    byte[] padBytes = new byte[padding];
                                    fs.Write(padBytes, 0, padBytes.Length);
                                    currentOffset += padding;
                                }

                                headers.Add(header);
                                OnFilePacked(filePath);
                            }
                        }

                        fs.Seek(0, SeekOrigin.Begin);
                        writer.Write(AFS_MAGIC);
                        writer.Write((uint)allFiles.Length);

                        foreach (var header in headers)
                        {
                            writer.Write(header.Offset);
                            writer.Write(header.Size);
                        }
                    }

                    if (File.Exists(outputAfsPath))
                    {
                        FileInfo fileInfo = new FileInfo(outputAfsPath);
                        PackingProgress?.Invoke(this, $"打包完成!");
                        PackingProgress?.Invoke(this, $"输出文件:{Path.GetFileName(outputAfsPath)}");
                        PackingProgress?.Invoke(this, $"文件大小:{FormatFileSize(fileInfo.Length)}");
                        OnPackingCompleted();
                    }
                    else
                    {
                        throw new FileNotFoundException("AFS打包过程未生成输出文件", outputAfsPath);
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                PackingError?.Invoke(this, "打包操作已取消");
                OnPackingFailed("打包操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"打包失败:{ex.Message}");
                OnPackingFailed($"打包失败:{ex.Message}");
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
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

        public void Repack(string directoryPath)
        {
            RepackAsync(directoryPath).Wait();
        }

        public override void Extract(string directoryPath)
        {
            Repack(directoryPath);
        }

        private struct AfsFileHeader
        {
            public uint Offset;
            public uint Size;
        }
    }
}
