using System.Text;

namespace super_toolbox
{
    public class KSCL_Extractor : BaseExtractor
    {
        private struct Header
        {
            public long KSCLLength;
            public int TexCount;
            public int HeaderlessSize;
            public int TablePointer;
            public int NameLength;
            public int NameCount;
        }

        private struct KSLT_Texture
        {
            public string Name;
            public int Pointer;
            public int FormatType;
            public short Width;
            public short Height;
            public int RawSize;
            public byte[] RawData;
        }

        private static readonly byte[] R8G8B8A8_Header = new byte[] {
            0x44,0x44,0x53,0x20,0x7C,0x00,0x00,0x00,0x07,0x10,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x41,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x20,0x00,0x00,0x00,0x04,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00
        };

        private static readonly byte[] BC3DXT5_Header = new byte[] {
            0x44,0x44,0x53,0x20,0x7C,0x00,0x00,0x00,0x07,0x10,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x41,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x20,0x00,0x00,0x00,0x04,0x00,0x00,0x00,
            0x44,0x58,0x54,0x35,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00
        };

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                OnExtractionFailed($"错误:目录不存在 {directoryPath}");
                return;
            }

            string extractedRootDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedRootDir);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var ksclFiles = Directory.GetFiles(directoryPath, "*.kscl", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedRootDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = ksclFiles.Count;

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(ksclFiles, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    }, ksclFilePath =>
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string exportPath = Path.Combine(extractedRootDir, Path.GetFileNameWithoutExtension(ksclFilePath));
                            Directory.CreateDirectory(exportPath);

                            int extractedCount = UnpackSingleKSCL(ksclFilePath, exportPath);

                            // 为每个生成的DDS文件调用OnFileExtracted（参考G1T提取器的做法）
                            var ddsFiles = Directory.GetFiles(exportPath, "*.dds", SearchOption.AllDirectories);
                            foreach (var ddsFile in ddsFiles)
                            {
                                OnFileExtracted(ddsFile);
                            }

                            // 可选：添加进度信息
                            System.Diagnostics.Debug.WriteLine($"处理完成: {Path.GetFileName(ksclFilePath)} -> 生成 {ddsFiles.Length} 个DDS文件");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"处理文件 {Path.GetFileName(ksclFilePath)} 失败: {ex.Message}");
                        }
                    });
                }, cancellationToken);

                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                OnExtractionFailed("用户取消了操作");
            }
            catch (Exception ex)
            {
                OnExtractionFailed($"提取过程出错: {ex.Message}");
            }
            finally
            {
                sw.Stop();
            }
        }

        private int UnpackSingleKSCL(string filePath, string exportPath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            {
                BinaryReader reader = new BinaryReader(stream);
                Header header = ReadHeader(ref reader);
                KSLT_Texture[] textures = GetTextures(ref reader, header);

                for (int i = 0; i < textures.Length; i++)
                {
                    byte[] texBytes = new byte[0x80 + textures[i].RawSize];
                    byte[] ddsHeader = GetDDSHeader(textures[i].FormatType);
                    ddsHeader.CopyTo(texBytes, 0);

                    using (MemoryStream texStream = new MemoryStream(texBytes))
                    using (BinaryWriter writer = new BinaryWriter(texStream))
                    {
                        writer.BaseStream.Seek(0xC, SeekOrigin.Begin);
                        writer.Write((int)textures[i].Height);
                        writer.Write((int)textures[i].Width);
                        writer.Write(textures[i].RawSize);
                        writer.BaseStream.Position = 0x80;
                        writer.Write(textures[i].RawData);
                        File.WriteAllBytes(Path.Combine(exportPath, $"{textures[i].Name}.dds"), texStream.ToArray());
                    }
                }

                return textures.Length;
            }
        }

        private static Header ReadHeader(ref BinaryReader reader)
        {
            reader.BaseStream.Seek(0, SeekOrigin.Begin);
            Header header = new Header();
            int ksclMagic = reader.ReadInt32();
            if (ksclMagic != 0x4B53434C) throw new Exception("不支持的文件类型");
            reader.ReadInt32();
            header.KSCLLength = reader.ReadInt32() + 0x8C;
            reader.BaseStream.Position = header.KSCLLength;
            int ksltMagic = reader.ReadInt32();
            reader.ReadInt32();
            if (ksltMagic != 0x4B534C54) throw new Exception("不支持的文件类型");
            header.TexCount = reader.ReadInt32();
            header.HeaderlessSize = reader.ReadInt32();
            header.TablePointer = reader.ReadInt32();
            header.NameLength = reader.ReadInt32();
            header.NameCount = reader.ReadInt32();
            return header;
        }

        private static string ReadString(ref BinaryReader reader)
        {
            StringBuilder str = new StringBuilder();
            byte[] ch = reader.ReadBytes(1);
            while (ch[0] != 0 && reader.BaseStream.Position < reader.BaseStream.Length)
            {
                str.Append(Encoding.ASCII.GetString(ch));
                ch = reader.ReadBytes(1);
            }
            return str.ToString();
        }

        private static byte[] GetDDSHeader(int format)
        {
            switch (format)
            {
                case 0: return R8G8B8A8_Header;
                case 3: return BC3DXT5_Header;
                default: throw new Exception($"不支持的格式类型: {format}");
            }
        }

        private static KSLT_Texture[] GetTextures(ref BinaryReader reader, Header header)
        {
            reader.BaseStream.Position = header.TablePointer + header.KSCLLength + 0x40 + (0x14 * header.TexCount);
            string[] names = new string[header.TexCount];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = ReadString(ref reader);
            }

            reader.BaseStream.Position = header.TablePointer + header.KSCLLength + 0x40;
            KSLT_Texture[] textures = new KSLT_Texture[header.TexCount];
            for (int i = 0; i < textures.Length; i++)
            {
                textures[i].Pointer = reader.ReadInt32();
                textures[i].Name = names[i];
                long temp = reader.BaseStream.Position + 0x10;
                reader.BaseStream.Position = textures[i].Pointer + header.KSCLLength;
                textures[i].FormatType = reader.ReadInt32();
                textures[i].Width = reader.ReadInt16();
                textures[i].Height = reader.ReadInt16();
                reader.BaseStream.Position += 0x14;
                textures[i].RawSize = reader.ReadInt32();
                reader.BaseStream.Position += 0x28;
                textures[i].RawData = reader.ReadBytes(textures[i].RawSize);
                reader.BaseStream.Position = temp;
            }
            return textures;
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}