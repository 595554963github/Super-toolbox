using System.Text;

namespace super_toolbox
{
    public class Xbox360_iso_packer : BaseExtractor
    {
        public new event EventHandler<string>? PackingStarted;
        public new event EventHandler<string>? PackingProgress;
        public new event EventHandler<string>? PackingError;

        private static readonly byte[] XSFHeader = new byte[]
        {
            0x58, 0x53, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        private class DirEntry
        {
            public int sector;
            public int length;
            public DirEntry(int sector, int length)
            {
                this.sector = sector;
                this.length = length;
            }
            public long StartPos() => sector * 2048L;
            public long EndPos() => (sector * 2048L) + length;
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await ConvertGodToIsoAsync(directoryPath, cancellationToken);
        }

        public async Task ConvertGodToIsoAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                PackingError?.Invoke(this, $"错误:目录{directoryPath}不存在");
                OnPackingFailed($"错误:目录{directoryPath}不存在");
                return;
            }

            var godIndexFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(f => string.IsNullOrEmpty(Path.GetExtension(f)))
                .Where(f => Directory.Exists(f + ".data"))
                .ToList();

            if (godIndexFiles.Count == 0)
            {
                PackingError?.Invoke(this, "未找到Xbox 360 GOD格式文件(无后缀文件+.data文件夹)");
                OnPackingFailed("未找到Xbox 360 GOD格式文件");
                return;
            }

            TotalFilesToPack = godIndexFiles.Count;

            PackingStarted?.Invoke(this, $"开始转换{godIndexFiles.Count}个GOD文件到ISO格式");
            PackingProgress?.Invoke(this, "找到的GOD文件列表:");

            foreach (var file in godIndexFiles)
            {
                string relativePath = GetRelativePath(directoryPath, file);
                PackingProgress?.Invoke(this, $"  {Path.GetFileName(file)}");
            }

            try
            {
                await ProcessGodFiles(godIndexFiles, directoryPath, cancellationToken);
                OnPackingCompleted();
            }
            catch (OperationCanceledException)
            {
                PackingError?.Invoke(this, "转换操作已取消");
                OnPackingFailed("转换操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"转换失败:{ex.Message}");
                OnPackingFailed($"转换失败:{ex.Message}");
            }
        }

        private async Task ProcessGodFiles(List<string> godIndexFiles, string baseDirectory, CancellationToken cancellationToken)
        {
            int totalFiles = godIndexFiles.Count;
            int processedCount = 0;

            foreach (var godIndexFile in godIndexFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);

                try
                {
                    processedCount++;
                    string baseName = Path.GetFileName(godIndexFile);
                    string dataPath = godIndexFile + ".data";
                    string outputDir = Path.GetDirectoryName(godIndexFile) ?? baseDirectory;

                    PackingProgress?.Invoke(this, $"[{processedCount}/{totalFiles}]正在处理:{baseName}");

                    if (!Directory.Exists(dataPath))
                    {
                        PackingError?.Invoke(this, $"错误:找不到数据文件夹 {baseName}.data");
                        continue;
                    }

                    int dataFileCount = 0;
                    while (File.Exists(Path.Combine(dataPath, $"Data{dataFileCount:D4}")))
                    {
                        dataFileCount++;
                    }

                    if (dataFileCount == 0)
                    {
                        PackingError?.Invoke(this, $"错误:{baseName}.data文件夹中没有Data文件");
                        continue;
                    }

                    PackingProgress?.Invoke(this, $"找到{dataFileCount}个数据文件");

                    bool hasXSF = HasXSFHeader(Path.Combine(dataPath, "Data0000"));

                    string isoPath = Path.Combine(outputDir, baseName + ".iso");

                    using (FileStream iso = new FileStream(isoPath, FileMode.Create, FileAccess.ReadWrite))
                    {
                        if (!hasXSF)
                        {
                            iso.Write(XSFHeader, 0, XSFHeader.Length);
                            PackingProgress?.Invoke(this, $"添加XSF头");
                        }

                        for (int fileNum = 0; fileNum < dataFileCount; fileNum++)
                        {
                            ThrowIfCancellationRequested(cancellationToken);

                            string dataFilePath = Path.Combine(dataPath, $"Data{fileNum:D4}");
                            PackingProgress?.Invoke(this, $"处理数据文件:Data{fileNum:D4}");

                            using (FileStream data = new FileStream(dataFilePath, FileMode.Open, FileAccess.Read))
                            {
                                data.Position = 0x2000;
                                int len = 0;

                                while (true)
                                {
                                    byte[] buff = new byte[0xCC000];
                                    len = await data.ReadAsync(buff, 0, buff.Length, cancellationToken);
                                    await iso.WriteAsync(buff, 0, len, cancellationToken);

                                    if (len < 0xCC000) break;

                                    len = await data.ReadAsync(buff, 0, 0x1000, cancellationToken);
                                    if (len < 0x1000) break;
                                }
                            }

                            OnFilePacked(dataFilePath);
                        }

                        if (!hasXSF)
                        {
                            await FixXFSHeaderAsync(iso, cancellationToken);
                            await FixSectorOffsetsAsync(iso, godIndexFile, cancellationToken);
                        }
                    }

                    PackingProgress?.Invoke(this, $"成功创建:{baseName}.iso");
                    OnFilePacked(isoPath);
                }
                catch (Exception ex)
                {
                    PackingError?.Invoke(this, $"处理{Path.GetFileName(godIndexFile)}失败:{ex.Message}");
                }
            }
        }

        private bool HasXSFHeader(string file)
        {
            try
            {
                using (FileStream data = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    data.Position = 0x2000;
                    byte[] buff = new byte[3];
                    data.Read(buff, 0, 3);

                    return buff[0] == 0x58 && buff[1] == 0x53 && buff[2] == 0x46;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task FixXFSHeaderAsync(FileStream iso, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            iso.Position = 8;
            byte[] bytes = BitConverter.GetBytes(iso.Length - 0x400);
            await iso.WriteAsync(bytes, 0, bytes.Length, cancellationToken);

            iso.Position = 0x8050;
            bytes = BitConverter.GetBytes((uint)(iso.Length / 2048));
            await iso.WriteAsync(bytes, 0, bytes.Length, cancellationToken);

            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                iso.WriteByte(bytes[i]);
            }
        }

        private async Task FixSectorOffsetsAsync(FileStream iso, string godPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                byte[] buffer = await File.ReadAllBytesAsync(godPath, cancellationToken);

                if ((buffer[0x391] & 0x40) != 0x40) return;

                int offset = BitConverter.ToInt32(buffer, 0x395);
                if (offset == 0) return;
                offset = offset * 2 - 34;

                Queue<DirEntry> directories = new Queue<DirEntry>();
                byte[] sectorBuffer = new byte[4];

                iso.Position = 0x10014;
                await iso.ReadAsync(sectorBuffer, 0, 4, cancellationToken);
                int sector = BitConverter.ToInt32(sectorBuffer, 0);

                if (sector > 0)
                {
                    sector -= offset;
                    byte[] corrected = BitConverter.GetBytes(sector);
                    iso.Position -= 4;
                    await iso.WriteAsync(corrected, 0, 4, cancellationToken);

                    await iso.ReadAsync(sectorBuffer, 0, 4, cancellationToken);
                    int size = BitConverter.ToInt32(sectorBuffer, 0);
                    directories.Enqueue(new DirEntry(sector, size));
                }

                while (directories.Count > 0)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    DirEntry dirEntry = directories.Dequeue();
                    iso.Position = dirEntry.StartPos();

                    while ((iso.Position + 4) < dirEntry.EndPos())
                    {
                        ThrowIfCancellationRequested(cancellationToken);

                        if ((iso.Position + 4) / 2048L > iso.Position / 2048L)
                        {
                            iso.Position += 2048L - (iso.Position % 2048L);
                        }

                        await iso.ReadAsync(sectorBuffer, 0, 4, cancellationToken);
                        if (sectorBuffer[0] == 0xff && sectorBuffer[1] == 0xff &&
                            sectorBuffer[2] == 0xff && sectorBuffer[3] == 0xff)
                        {
                            if (dirEntry.EndPos() - iso.Position > 2048)
                            {
                                iso.Position += 2048L - (iso.Position % 2048L);
                                continue;
                            }
                            break;
                        }

                        await iso.ReadAsync(sectorBuffer, 0, 4, cancellationToken);
                        sector = BitConverter.ToInt32(sectorBuffer, 0);

                        if (sector > 0)
                        {
                            sector -= offset;
                            byte[] corrected = BitConverter.GetBytes(sector);
                            iso.Position -= 4;
                            await iso.WriteAsync(corrected, 0, 4, cancellationToken);
                        }

                        await iso.ReadAsync(sectorBuffer, 0, 4, cancellationToken);
                        int size = BitConverter.ToInt32(sectorBuffer, 0);

                        byte[] attrBuffer = new byte[1];
                        await iso.ReadAsync(attrBuffer, 0, 1, cancellationToken);

                        if ((attrBuffer[0] & 0x10) == 0x10)
                        {
                            directories.Enqueue(new DirEntry(sector, size));
                        }

                        byte[] nameLenBuffer = new byte[1];
                        await iso.ReadAsync(nameLenBuffer, 0, 1, cancellationToken);

                        iso.Position += nameLenBuffer[0];

                        if ((14 + nameLenBuffer[0]) % 4 > 0)
                        {
                            iso.Position += 4 - ((14 + nameLenBuffer[0]) % 4);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PackingError?.Invoke(this, $"修复扇区偏移失败:{ex.Message}");
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

        public void ConvertGodToIso(string directoryPath)
        {
            ConvertGodToIsoAsync(directoryPath).Wait();
        }

        public override void Extract(string directoryPath)
        {
            ConvertGodToIso(directoryPath);
        }
    }
}