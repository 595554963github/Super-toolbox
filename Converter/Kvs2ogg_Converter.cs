using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Kvs2ogg_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public const int HEADER_SIZE = 32;
        public const int STREAM_CHUNK_SIZE = 1024 * 1024;

        public static readonly byte[][] VALID_MAGIC = new byte[][]
        {
            new byte[] { 0x4B, 0x4F, 0x56, 0x53 },
            new byte[] { 0x4B, 0x56, 0x53, 0x00 }
        };

        public struct HeaderValues
        {
            public uint Magic;
            public int Size;
            public int LoopStartSamples;
            public int LoopEndSamples;
            public int Unknown1;
            public int Unknown2;
            public int Unknown3;
            public int Unknown4;

            public static HeaderValues FromBytes(byte[] data)
            {
                if (data.Length < HEADER_SIZE)
                    throw new InvalidDataException("文件太小");

                return new HeaderValues
                {
                    Magic = BitConverter.ToUInt32(data, 0),
                    Size = BitConverter.ToInt32(data, 4),
                    LoopStartSamples = BitConverter.ToInt32(data, 8),
                    LoopEndSamples = BitConverter.ToInt32(data, 12),
                    Unknown1 = BitConverter.ToInt32(data, 16),
                    Unknown2 = BitConverter.ToInt32(data, 20),
                    Unknown3 = BitConverter.ToInt32(data, 24),
                    Unknown4 = BitConverter.ToInt32(data, 28)
                };
            }
        }

        public static bool IsValidMagic(byte[] magic)
        {
            foreach (var valid in VALID_MAGIC)
            {
                if (magic.SequenceEqual(valid))
                    return true;
            }
            return false;
        }

        public static (HeaderValues header, byte[] trailer) ReadKvsContainer(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            HeaderValues header = HeaderValues.FromBytes(data);

            byte[] headerMagic = new byte[4];
            Array.Copy(data, 0, headerMagic, 0, 4);

            if (!IsValidMagic(headerMagic))
                throw new InvalidDataException($"无效的KVS文件头");

            int payloadEnd = HEADER_SIZE + header.Size;
            if (payloadEnd > data.Length)
                throw new InvalidDataException("负载大小不匹配");

            byte[] trailer = new byte[data.Length - payloadEnd];
            Array.Copy(data, payloadEnd, trailer, 0, trailer.Length);

            return (header, trailer);
        }

        public static void XorChunkInPlace(byte[] chunk, int startOffset)
        {
            int limit = Math.Min(chunk.Length, Math.Max(0, 0x100 - startOffset));
            for (int i = 0; i < limit; i++)
            {
                chunk[i] ^= (byte)((startOffset + i) & 0xFF);
            }
        }

        public static void CopyXorStream(Stream reader, Stream writer, int size)
        {
            int remaining = size;
            int offset = 0;
            byte[] buffer = new byte[STREAM_CHUNK_SIZE];

            while (remaining > 0)
            {
                int toRead = Math.Min(STREAM_CHUNK_SIZE, remaining);
                int read = reader.Read(buffer, 0, toRead);
                if (read == 0)
                    throw new InvalidDataException("读取音频负载时遇到意外的文件结尾");

                XorChunkInPlace(buffer, offset);
                writer.Write(buffer, 0, read);

                offset += read;
                remaining -= read;
            }
        }

        public static bool DecryptFile(string sourcePath, string targetPath)
        {
            try
            {
                var (header, _) = ReadKvsContainer(sourcePath);

                using var reader = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                using var writer = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

                reader.Seek(HEADER_SIZE, SeekOrigin.Begin);
                CopyXorStream(reader, writer, header.Size);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解密失败: {ex.Message}");
                return false;
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    var kvsFiles = Directory.GetFiles(directoryPath, "*.kvs", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(directoryPath, "*.kovs", SearchOption.AllDirectories))
                        .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                        .ToArray();

                    TotalFilesToConvert = kvsFiles.Length;
                    int successCount = 0;

                    if (kvsFiles.Length == 0)
                    {
                        ConversionError?.Invoke(this, "未找到需要转换的KVS文件");
                        OnConversionFailed("未找到需要转换的KVS文件");
                        return;
                    }

                    ConversionStarted?.Invoke(this, $"开始转换,共{TotalFilesToConvert}个KVS文件");

                    foreach (var kvsFilePath in kvsFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string fileName = Path.GetFileNameWithoutExtension(kvsFilePath);
                        ConversionProgress?.Invoke(this, $"正在转换:{fileName}.kvs");

                        string fileDirectory = Path.GetDirectoryName(kvsFilePath) ?? string.Empty;
                        string oggFilePath = Path.Combine(fileDirectory, $"{fileName}.ogg");

                        try
                        {
                            if (DecryptFile(kvsFilePath, oggFilePath))
                            {
                                successCount++;
                                ConversionProgress?.Invoke(this, $"已转换:{fileName}.ogg");
                                OnFileConverted(oggFilePath);
                            }
                            else
                            {
                                ConversionError?.Invoke(this, $"{fileName}.kvs转换失败");
                                OnConversionFailed($"{fileName}.kvs转换失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                            OnConversionFailed($"{fileName}.kvs处理错误:{ex.Message}");
                        }
                    }

                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                    OnConversionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "转换操作已取消");
                OnConversionFailed("转换操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换失败:{ex.Message}");
                OnConversionFailed($"转换失败:{ex.Message}");
            }
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}