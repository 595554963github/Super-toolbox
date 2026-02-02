using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace super_toolbox
{
    public class Farc_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private class FARCEntry
        {
            public string Name { get; set; } = string.Empty;
            public uint Offset { get; set; }
            public uint? ZSize { get; set; }
            public uint Size { get; set; }
        }

        private static readonly byte[] FARC_KEY = Encoding.ASCII.GetBytes("project_diva.bin");

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            List<string> extractedFiles = new List<string>();

            foreach (var file in files)
            {
                ThrowIfCancellationRequested(cancellationToken);

                try
                {
                    byte[] magic = await ReadBytesAsync(file, 0, 4, cancellationToken);

                    if (magic.Length >= 4 &&
                        (magic[0] == 'F' && magic[1] == 'A' &&
                         (magic[2] == 'R' || magic[2] == 'r') &&
                         (magic[3] == 'C' || magic[3] == 'c')))
                    {
                        ExtractionProgress?.Invoke(this, $"处理FARC文件:{Path.GetFileName(file)}");
                        await ExtractFARCFile(file, directoryPath, extractedFiles, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{file}时出错:{e.Message}");
                }
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到FARC文件");
            }
            OnExtractionCompleted();
        }

        private async Task<byte[]> ReadBytesAsync(string file, int offset, int count, CancellationToken cancellationToken)
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
            fs.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[count];
            await fs.ReadAsync(buffer.AsMemory(0, count), cancellationToken);
            return buffer;
        }

        private async Task ExtractFARCFile(string file, string baseDir, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            byte[] content = await File.ReadAllBytesAsync(file, cancellationToken);

            using var ms = new MemoryStream(content);
            using var br = new BinaryReader(ms);

            byte[] magic = br.ReadBytes(4);
            uint limit = ReadBigEndian(br);

            bool isCompressed = magic[3] != 'c';
            bool isEncrypted = magic[2] == 'R' && magic[3] == 'C';

            br.ReadUInt32();
            if (isEncrypted) br.ReadUInt32();
            if (isEncrypted) ms.Seek(8, SeekOrigin.Current);

            ms.Seek(isEncrypted ? 15 : 12, SeekOrigin.Begin);

            var entries = new List<FARCEntry>();
            while (ms.Position < limit)
            {
                string name = ReadNullTerminatedString(br);
                if (string.IsNullOrEmpty(name)) break;

                uint offset = ReadBigEndian(br);
                uint? zsize = isCompressed ? ReadBigEndian(br) : (uint?)null;
                uint size = ReadBigEndian(br);

                if (!string.IsNullOrEmpty(name) && size > 0)
                {
                    entries.Add(new FARCEntry { Name = name, Offset = offset, ZSize = zsize, Size = size });
                }
            }

            string outputDir = Path.Combine(Path.GetDirectoryName(file) ?? baseDir, Path.GetFileNameWithoutExtension(file));
            Directory.CreateDirectory(outputDir);

            string type = isEncrypted ? "FARC" : (isCompressed ? "FArC" : "FArc");
            ExtractionProgress?.Invoke(this, $"类型:{type}");
            ExtractionProgress?.Invoke(this, "文件列表:");
            foreach (var entry in entries)
            {
                ExtractionProgress?.Invoke(this, $"-- {entry.Name} (大小:{entry.Size}字节)");
            }

            foreach (var entry in entries)
            {
                if (entry.Offset + entry.Size > content.Length) continue;

                byte[] data;
                if (!isCompressed || (entry.ZSize.HasValue && entry.Size == entry.ZSize.Value))
                {
                    data = new byte[entry.Size];
                    Array.Copy(content, entry.Offset, data, 0, entry.Size);
                }
                else
                {
                    uint alignedZSize = entry.ZSize ?? 0;
                    if (alignedZSize % 16 != 0) alignedZSize += 16 - (alignedZSize % 16);

                    byte[] compressed = new byte[alignedZSize];
                    Array.Copy(content, entry.Offset, compressed, 0, alignedZSize);

                    if (isEncrypted)
                    {
                        using var aes = Aes.Create();
                        aes.Key = FARC_KEY;
                        aes.Mode = CipherMode.ECB;
                        aes.Padding = PaddingMode.None;
                        using var decryptor = aes.CreateDecryptor();
                        compressed = decryptor.TransformFinalBlock(compressed, 0, compressed.Length);
                    }

                    byte[] trimmed = new byte[entry.ZSize ?? 0];
                    Array.Copy(compressed, trimmed, trimmed.Length);

                    try
                    {
                        using var ms2 = new MemoryStream(trimmed);
                        using var gzip = new GZipStream(ms2, CompressionMode.Decompress);
                        using var ms3 = new MemoryStream();
                        gzip.CopyTo(ms3);
                        data = ms3.ToArray();

                        if (data.Length > entry.Size)
                        {
                            byte[] final = new byte[entry.Size];
                            Array.Copy(data, final, final.Length);
                            data = final;
                        }
                    }
                    catch
                    {
                        ExtractionProgress?.Invoke(this, $"解压失败:{entry.Name}");
                        continue;
                    }
                }

                string outputPath = Path.Combine(outputDir, entry.Name);
                string outDir = Path.GetDirectoryName(outputPath) ?? outputDir;
                Directory.CreateDirectory(outDir);

                outputPath = GetUniquePath(outputPath);
                await File.WriteAllBytesAsync(outputPath, data, cancellationToken);
                extractedFiles.Add(outputPath);
                OnFileExtracted(outputPath);
                ExtractionProgress?.Invoke(this, $"已提取:{entry.Name}");
            }
        }

        private uint ReadBigEndian(BinaryReader br)
        {
            var bytes = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private string ReadNullTerminatedString(BinaryReader br)
        {
            var sb = new StringBuilder();
            while (true)
            {
                byte b = br.ReadByte();
                if (b == 0) break;
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        private string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int count = 1;

            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name}_{count}{ext}");
                count++;
            } while (File.Exists(newPath));

            return newPath;
        }
    }
}