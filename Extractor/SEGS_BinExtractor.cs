using ICSharpCode.SharpZipLib.Zip.Compression;
using System.Collections.Concurrent;

namespace super_toolbox
{
    public class SEGS_BinExtractor : BaseExtractor
    {
        private static readonly byte[] SEGS_SIGNATURE = { 0x73, 0x65, 0x67, 0x73 }; // "segs"
        private static readonly byte[] FPAC_SIGNATURE = { 0x46, 0x50, 0x41, 0x43 }; // "FPAC"
        private static readonly byte[] UNICODE_SIGNATURE = { 0xFE, 0xFF, 0x00, 0x00 };
        private static readonly byte[] NULL_SIGNATURE = { 0x00, 0x00, 0x00, 0x00 };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                Console.WriteLine("目录路径不能为空.");
                return;
            }

            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var files = Directory.EnumerateFiles(directoryPath, "*.bin", SearchOption.AllDirectories)
               .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
               .ToList();

            TotalFilesToExtract = files.Count;

            var extractedFiles = new ConcurrentBag<string>();

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, filePath =>
                    {
                        try
                        {
                            Console.WriteLine($"正在处理文件:{filePath}");
                            using (BinaryReader input = new BinaryReader(File.OpenRead(filePath)))
                            {
                                int n = 1;

                                while (input.BaseStream.Position < input.BaseStream.Length)
                                {
                                    long currentPos = input.BaseStream.Position;
                                    byte[] check = input.ReadBytes(4);

                                    if (check.Length < 4)
                                        break;

                                    if (check.SequenceEqual(SEGS_SIGNATURE))
                                    {
                                        input.BaseStream.Position = currentPos;
                                        long basePos = input.BaseStream.Position;

                                        SEGSHDR hdr = ReadSEGSHDR(input);
                                        List<SEGSENTRY> entries = ReadSEGSENTRIES(input, hdr.entry_count);

                                        byte[] fullBuff = new byte[hdr.original_length];
                                        int p = 0;

                                        foreach (var entry in entries)
                                        {
                                            uint len = entry.length;
                                            uint outLen = entry.original_length;
                                            bool compressed = len != outLen;

                                            if (len == 0) len = 65536;
                                            if (outLen == 0) outLen = 65536;

                                            long offset = (basePos + entry.offset) & ~1L;
                                            byte[] buff = ReadBytes(input, (int)len, offset);

                                            if (compressed)
                                            {
                                                try
                                                {
                                                    buff = Inflate(buff, (int)outLen);
                                                }
                                                catch (Exception ex)
                                                {
                                                    Console.WriteLine($"解压缩失败:{ex.Message}");
                                                    continue;
                                                }
                                            }

                                            Buffer.BlockCopy(buff, 0, fullBuff, p, buff.Length);
                                            p += buff.Length;
                                        }

                                        string extension = GuessExtension(fullBuff);
                                        string filename = Path.GetFileNameWithoutExtension(filePath) + $"_{n}{extension}";
                                        string outputFilePath = Path.Combine(extractedDir, filename);

                                        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? extractedDir);
                                        File.WriteAllBytes(outputFilePath, fullBuff);
                                        extractedFiles.Add(outputFilePath);
                                        OnFileExtracted(outputFilePath);

                                        input.BaseStream.Position = basePos + hdr.length;
                                        n++;
                                    }
                                    else if (check.SequenceEqual(FPAC_SIGNATURE))
                                    {
                                        input.BaseStream.Position = currentPos;
                                        FPACHDR hdr = ReadFPACHDR(input);

                                        byte[] fileData = ReadBytes(input, (int)hdr.file_length, currentPos);

                                        string filename = Path.GetFileNameWithoutExtension(filePath) + $"_{n}.pac";
                                        string outputFilePath = Path.Combine(extractedDir, filename);

                                        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? extractedDir);
                                        File.WriteAllBytes(outputFilePath, fileData);
                                        extractedFiles.Add(outputFilePath);
                                        OnFileExtracted(outputFilePath);

                                        input.BaseStream.Position = currentPos + hdr.file_length;
                                        n++;
                                    }
                                    else if (!check.SequenceEqual(UNICODE_SIGNATURE) &&
                                             !check.SequenceEqual(NULL_SIGNATURE))
                                    {
                                        Console.WriteLine($"未知数据块@{currentPos}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"错误处理{filePath}:{ex.Message}");
                        }
                    });
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("提取已取消");
            }

            sw.Stop();
            Console.WriteLine($"在{sw.Elapsed.TotalSeconds:F2}秒内完成");
            Console.WriteLine($"提取文件:{ExtractedFileCount}");
        }
        private SEGSHDR ReadSEGSHDR(BinaryReader input)
        {
            return new SEGSHDR
            {
                signature = input.ReadBytes(4),
                unknown1 = ReadUInt16BE(input),
                entry_count = ReadUInt16BE(input),
                original_length = ReadUInt32BE(input),
                length = ReadUInt32BE(input)
            };
        }
        private List<SEGSENTRY> ReadSEGSENTRIES(BinaryReader input, ushort entryCount)
        {
            List<SEGSENTRY> entries = new List<SEGSENTRY>();
            for (int i = 0; i < entryCount; i++)
            {
                entries.Add(new SEGSENTRY
                {
                    length = ReadUInt16BE(input),
                    original_length = ReadUInt16BE(input),
                    offset = ReadUInt32BE(input)
                });
            }
            return entries;
        }
        private FPACHDR ReadFPACHDR(BinaryReader input)
        {
            return new FPACHDR
            {
                signature = input.ReadBytes(4),
                data_base = ReadUInt32BE(input),
                file_length = ReadUInt32BE(input),
                entry_count = ReadUInt32BE(input),
                unknown1 = ReadUInt32BE(input),
                filename_length = ReadUInt32BE(input),
                unknown3 = ReadUInt32BE(input),
                unknown4 = ReadUInt32BE(input)
            };
        }
        private byte[] ReadBytes(BinaryReader input, int length, long offset)
        {
            long originalPosition = input.BaseStream.Position;
            input.BaseStream.Position = offset;
            byte[] buffer = input.ReadBytes(length);
            input.BaseStream.Position = originalPosition;
            return buffer;
        }
        private byte[] Inflate(byte[] input, int outputLength)
        {
            try
            {
                Inflater inflater = new Inflater(true);
                inflater.SetInput(input);
                byte[] output = new byte[outputLength];
                inflater.Inflate(output);
                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解压缩失败:{ex.Message}");
                return input;
            }
        }
        private string GuessExtension(byte[] buff)
        {
            if (buff.Length >= 4 && buff.Take(4).SequenceEqual(FPAC_SIGNATURE))
            {
                return ".pac";
            }
            return ".dat";
        }
        private ushort ReadUInt16BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt16(bytes, 0);
        }
        private uint ReadUInt32BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }
    }
    public struct SEGSHDR
    {
        public byte[] signature;
        public ushort unknown1;
        public ushort entry_count;
        public uint original_length;
        public uint length;
    }
    public struct SEGSENTRY
    {
        public ushort length;
        public ushort original_length;
        public uint offset;
    }
    public struct FPACHDR
    {
        public byte[] signature;
        public uint data_base;
        public uint file_length;
        public uint entry_count;
        public uint unknown1;
        public uint filename_length;
        public uint unknown3;
        public uint unknown4;
    }
}