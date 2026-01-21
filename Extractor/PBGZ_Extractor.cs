using System.Text;

namespace super_toolbox
{
    public class PBGZ_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> extractedFiles = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnExtractionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var datFiles = Directory.EnumerateFiles(directoryPath, "*.dat", SearchOption.AllDirectories);

            TotalFilesToExtract = datFiles.Count();
            int processedFiles = 0;

            foreach (var datFile in datFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(datFile)}");

                try
                {
                    await ProcessDatFile(datFile, extractedFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{datFile}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{datFile}时出错:{e.Message}");
                }

                processedFiles++;
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到有效文件");
            }
            OnExtractionCompleted();
        }

        private async Task ProcessDatFile(string datFilePath, List<string> extractedFiles, CancellationToken cancellationToken)
        {
            byte[] fileData = await File.ReadAllBytesAsync(datFilePath, cancellationToken);

            if (fileData.Length < 16 || Encoding.ASCII.GetString(fileData, 0, 4) != "PBGZ")
            {
                ExtractionError?.Invoke(this, $"文件{Path.GetFileName(datFilePath)}不是有效的TH08/TH09 DAT文件");
                return;
            }

            uint count = BitConverter.ToUInt32(fileData, 4);
            uint offset = BitConverter.ToUInt32(fileData, 8);
            uint size = BitConverter.ToUInt32(fileData, 12);

            byte[] headerBlock = new byte[12];
            Array.Copy(fileData, 4, headerBlock, 0, 12);
            ThDecrypt(headerBlock, 12, 0x1b, 0x37, 12, 0x400);

            count = BitConverter.ToUInt32(headerBlock, 0) - 123456;
            offset = BitConverter.ToUInt32(headerBlock, 4) - 345678;
            size = BitConverter.ToUInt32(headerBlock, 8) - 567891;

            uint zsize = (uint)fileData.Length - offset;
            byte[] zdata = new byte[zsize];
            Array.Copy(fileData, offset, zdata, 0, zsize);

            ThDecrypt(zdata, zsize, 0x3e, 0x9b, 0x80, 0x400);

            byte[] fileListData = ThUnlzss(zdata, zsize, size);

            List<ThDatEntry> entries = new List<ThDatEntry>();
            int pos = 0;

            for (uint i = 0; i < count && pos < fileListData.Length; i++)
            {
                int nameEnd = Array.IndexOf(fileListData, (byte)0, pos);
                if (nameEnd == -1 || nameEnd - pos > 255) break;

                string name = Encoding.ASCII.GetString(fileListData, pos, nameEnd - pos);
                pos = nameEnd + 1;

                if (pos + 12 > fileListData.Length) break;

                uint entryOffset = BitConverter.ToUInt32(fileListData, pos);
                uint entrySize = BitConverter.ToUInt32(fileListData, pos + 4);
                uint entryExtra = BitConverter.ToUInt32(fileListData, pos + 8);
                pos += 12;

                entries.Add(new ThDatEntry
                {
                    Name = name,
                    Offset = entryOffset,
                    Size = entrySize,
                    Extra = entryExtra
                });
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                if (i < entries.Count - 1)
                {
                    entry.ZSize = entries[i + 1].Offset - entry.Offset;
                }
                else
                {
                    entry.ZSize = (uint)fileData.Length - zsize - entry.Offset;
                }

                entries[i] = entry;
            }

            string outputDir = Path.Combine(Path.GetDirectoryName(datFilePath) ?? string.Empty,
                                           $"{Path.GetFileNameWithoutExtension(datFilePath)}");
            Directory.CreateDirectory(outputDir);

            using (FileStream fs = new FileStream(datFilePath, FileMode.Open, FileAccess.Read))
            {
                foreach (var entry in entries)
                {
                    fs.Seek(entry.Offset, SeekOrigin.Begin);
                    byte[] zentryData = new byte[entry.ZSize];
                    await fs.ReadAsync(zentryData, 0, (int)entry.ZSize, cancellationToken);

                    byte[] entryData = ThUnlzss(zentryData, entry.ZSize, entry.Size);

                    if (entryData.Length >= 4 && Encoding.ASCII.GetString(entryData, 0, 3) == "edz")
                    {
                        byte entryType = entryData[3];
                        string ext = Path.GetExtension(entry.Name).ToLower();

                        int typeIndex = -1;
                        if (ext == ".anm") typeIndex = 2;
                        else if (ext == ".ecl") typeIndex = 4;
                        else if (ext == ".jpg") typeIndex = 3;
                        else if (ext == ".msg") typeIndex = 0;
                        else if (ext == ".txt") typeIndex = 1;
                        else if (ext == ".wav") typeIndex = 5;
                        else typeIndex = 6;

                        CryptParams[] paramsArray = datFilePath.ToLower().Contains("th09") ? th09_crypt_params : th08_crypt_params;

                        if (typeIndex >= 0 && typeIndex < 8)
                        {
                            uint decryptSize = (uint)(entryData.Length - 4);
                            ThDecrypt(entryData, 4, decryptSize,
                                paramsArray[typeIndex].Key, paramsArray[typeIndex].Step,
                                paramsArray[typeIndex].Block, paramsArray[typeIndex].Limit);
                        }

                        string outPath = Path.Combine(outputDir, entry.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? outputDir);
                        await File.WriteAllBytesAsync(outPath, entryData.Skip(4).ToArray(), cancellationToken);
                        extractedFiles.Add(outPath);
                        OnFileExtracted(outPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{entry.Name}");
                    }
                    else
                    {
                        string outPath = Path.Combine(outputDir, entry.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? outputDir);
                        await File.WriteAllBytesAsync(outPath, entryData, cancellationToken);
                        extractedFiles.Add(outPath);
                        OnFileExtracted(outPath);
                        ExtractionProgress?.Invoke(this, $"已提取:{entry.Name}");
                    }
                }
            }
        }

        private struct ThDatEntry
        {
            public string Name;
            public uint Offset;
            public uint Size;
            public uint Extra;
            public uint ZSize;
        }

        private struct CryptParams
        {
            public byte Key;
            public byte Step;
            public uint Block;
            public uint Limit;
        }

        private static readonly CryptParams[] th08_crypt_params = new CryptParams[]
        {
            new CryptParams { Key = 0x1b, Step = 0x37, Block = 0x40, Limit = 0x2000 },
            new CryptParams { Key = 0x51, Step = 0xe9, Block = 0x40, Limit = 0x3000 },
            new CryptParams { Key = 0xc1, Step = 0x51, Block = 0x1400, Limit = 0x2000 },
            new CryptParams { Key = 0x03, Step = 0x19, Block = 0x1400, Limit = 0x7800 },
            new CryptParams { Key = 0xab, Step = 0xcd, Block = 0x200, Limit = 0x1000 },
            new CryptParams { Key = 0x12, Step = 0x34, Block = 0x400, Limit = 0x2800 },
            new CryptParams { Key = 0x35, Step = 0x97, Block = 0x80, Limit = 0x2800 },
            new CryptParams { Key = 0x99, Step = 0x37, Block = 0x400, Limit = 0x1000 }
        };

        private static readonly CryptParams[] th09_crypt_params = new CryptParams[]
        {
            new CryptParams { Key = 0x1b, Step = 0x37, Block = 0x40, Limit = 0x2800 },
            new CryptParams { Key = 0x51, Step = 0xe9, Block = 0x40, Limit = 0x3000 },
            new CryptParams { Key = 0xc1, Step = 0x51, Block = 0x400, Limit = 0x400 },
            new CryptParams { Key = 0x03, Step = 0x19, Block = 0x400, Limit = 0x400 },
            new CryptParams { Key = 0xab, Step = 0xcd, Block = 0x200, Limit = 0x1000 },
            new CryptParams { Key = 0x12, Step = 0x34, Block = 0x400, Limit = 0x400 },
            new CryptParams { Key = 0x35, Step = 0x97, Block = 0x80, Limit = 0x2800 },
            new CryptParams { Key = 0x99, Step = 0x37, Block = 0x400, Limit = 0x1000 }
        };

        private void ThDecrypt(byte[] data, uint size, byte key, byte step, uint block, uint limit)
        {
            if (data == null || size == 0) return;

            byte[] temp = new byte[block];
            uint increment = (block >> 1) + (block & 1);

            if (size < (block >> 2))
                size = 0;
            else
                size -= (size % block < (block >> 2)) ? size % block + size % 2 : 0u;

            if (limit % block != 0)
                limit += (block - (limit % block));

            uint end = (size < limit) ? size : limit;
            uint pos = 0;

            while (pos < end)
            {
                uint currentBlock = block;
                if (end - pos < block)
                {
                    currentBlock = end - pos;
                    increment = (currentBlock >> 1) + (currentBlock & 1);
                }

                int inIdx = (int)pos;
                int outIdx = (int)currentBlock - 1;

                while (outIdx > 0)
                {
                    temp[outIdx--] = (byte)(data[inIdx] ^ key);
                    temp[outIdx--] = (byte)(data[inIdx + increment] ^ (key + step * increment));
                    inIdx++;
                    key += step;
                }

                if ((currentBlock & 1) != 0)
                {
                    temp[outIdx] = (byte)(data[inIdx] ^ key);
                    key += step;
                }
                key += (byte)(step * increment);

                Array.Copy(temp, 0, data, (int)pos, (int)currentBlock);
                pos += currentBlock;
            }
        }

        private void ThDecrypt(byte[] data, int start, uint size, byte key, byte step, uint block, uint limit)
        {
            byte[] buffer = new byte[size];
            Array.Copy(data, start, buffer, 0, size);
            ThDecrypt(buffer, size, key, step, block, limit);
            Array.Copy(buffer, 0, data, start, size);
        }

        private byte[] ThUnlzss(byte[] input, uint inputSize, uint outputSize)
        {
            byte[] output = new byte[outputSize];
            byte[] dict = new byte[0x2000];
            uint dictHead = 1;
            uint bytesWritten = 0;

            BitStream bs = new BitStream(input);

            while (bytesWritten < outputSize)
            {
                if (bs.Read(1) != 0)
                {
                    byte c = (byte)bs.Read(8);
                    output[bytesWritten++] = c;
                    dict[dictHead] = c;
                    dictHead = (dictHead + 1) & 0x1FFF;
                }
                else
                {
                    uint matchOffset = bs.Read(13);
                    if (matchOffset == 0) break;

                    uint matchLen = bs.Read(4) + 3;

                    for (uint i = 0; i < matchLen && bytesWritten < outputSize; ++i)
                    {
                        byte c = dict[(matchOffset + i) & 0x1FFF];
                        output[bytesWritten++] = c;
                        dict[dictHead] = c;
                        dictHead = (dictHead + 1) & 0x1FFF;
                    }
                }
            }

            return output;
        }

        private class BitStream
        {
            private byte[] data;
            private int pos;
            private uint bits;
            private uint buffer;

            public BitStream(byte[] data)
            {
                this.data = data;
                pos = 0;
                bits = 0;
                buffer = 0;
            }

            public uint Read(int bitsToRead)
            {
                if (bitsToRead > 25)
                {
                    uint r = Read(24);
                    bitsToRead -= 24;
                    return (r << bitsToRead) | Read(bitsToRead);
                }

                while (bitsToRead > bits)
                {
                    if (pos >= data.Length) return 0;
                    buffer = (buffer << 8) | data[pos++];
                    bits += 8;
                }

                bits -= (uint)bitsToRead;
                uint mask = (uint)((1 << bitsToRead) - 1);
                return (buffer >> (int)bits) & mask;
            }
        }
    }
}