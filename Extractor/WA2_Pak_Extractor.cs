using System.Text;
using System.Drawing.Imaging;

namespace super_toolbox
{
    public class WA2_Pak_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] PACK_MAGIC = { 0x50, 0x41, 0x43, 0x4B };
        private static readonly byte[] LAC0_MAGIC = { 0x00, 0x43, 0x41, 0x4C };

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

            var filePaths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);

            TotalFilesToExtract = 0;

            foreach (var filePath in filePaths)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(filePath)}");

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(filePath, cancellationToken);

                    if (content.Length < 4)
                        continue;

                    uint magic = BitConverter.ToUInt32(content, 0);

                    if (magic == 0x5041434B)
                    {
                        await ProcessPACKFile(content, filePath, extractedFiles, cancellationToken);
                    }
                    else if (magic == 0x0043414C)
                    {
                        await ProcessLAC0File(content, filePath, extractedFiles, cancellationToken);
                    }
                    else
                    {
                        ExtractionProgress?.Invoke(this, $"跳过非WA2格式文件:{Path.GetFileName(filePath)}");
                    }
                }
                catch (OperationCanceledException)
                {
                    ExtractionError?.Invoke(this, "提取操作已取消");
                    OnExtractionFailed("提取操作已取消");
                    throw;
                }
                catch (IOException e)
                {
                    ExtractionError?.Invoke(this, $"读取文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"读取文件{filePath}时出错:{e.Message}");
                }
                catch (Exception e)
                {
                    ExtractionError?.Invoke(this, $"处理文件{filePath}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{filePath}时出错:{e.Message}");
                }
            }

            TotalFilesToExtract = extractedFiles.Count;
            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到WA2格式文件");
            }

            OnExtractionCompleted();
        }

        private async Task ProcessPACKFile(byte[] content, string filePath,
                                         List<string> extractedFiles, CancellationToken cancellationToken)
        {
            using (var stream = new MemoryStream(content))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 12;

                uint nentry = reader.ReadUInt32();

                string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                string outPath = Path.Combine(fileDirectory, Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outPath);

                ExtractionProgress?.Invoke(this, $"处理PACK文件:{Path.GetFileName(filePath)}, 包含{nentry}个文件");

                for (int i = 0; i < nentry; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    stream.Position = 16 + i * 44;

                    uint crypted = reader.ReadUInt32();
                    byte[] nameBytes = reader.ReadBytes(24);
                    stream.Position += 8;
                    uint offset = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();

                    string name = ToShiftJISString(nameBytes);

                    if (size == 0)
                    {
                        ExtractionProgress?.Invoke(this, $"跳过空文件:{name}");
                        continue;
                    }

                    long currentPos = stream.Position;

                    stream.Position = offset;
                    string fout = Path.Combine(outPath, name);

                    ExtractionProgress?.Invoke(this, $"提取:{name}");

                    using (var fs = new FileStream(fout, FileMode.Create))
                    {
                        if (crypted == 0)
                        {
                            if (offset + size > content.Length)
                            {
                                ExtractionError?.Invoke(this, $"文件{name}数据超出范围");
                                continue;
                            }

                            byte[] data = new byte[size];
                            Array.Copy(content, offset, data, 0, size);
                            await fs.WriteAsync(data, 0, data.Length, cancellationToken);
                        }
                        else
                        {
                            uint inceil = reader.ReadUInt32();
                            uint outceil = reader.ReadUInt32();

                            if (offset + 8 + inceil > content.Length)
                            {
                                ExtractionError?.Invoke(this, $"加密文件{name}数据超出范围");
                                continue;
                            }

                            byte[] arr = new byte[0x1000];
                            for (int j = 0; j < 0xFEE; j++)
                                arr[j] = 0x20;

                            uint insize = 0, outsize = 0;
                            int arr_w = 0xFEE;

                            while (insize < inceil && outsize < outceil)
                            {
                                ThrowIfCancellationRequested(cancellationToken);

                                byte flag = reader.ReadByte();
                                insize++;

                                for (int k = 0; k < 8; k++)
                                {
                                    if (insize >= inceil || outsize >= outceil)
                                        break;

                                    byte b1 = reader.ReadByte();
                                    insize++;

                                    if ((flag & 1) == 0)
                                    {
                                        byte b2 = reader.ReadByte();
                                        insize++;

                                        int arr_r = b1 | ((b2 & 0xF0) << 4);
                                        int counter = (b2 & 0xF) + 3;

                                        while (counter > 0 && outsize < outceil)
                                        {
                                            byte b = arr[arr_r & 0xFFF];
                                            arr[arr_w & 0xFFF] = b;
                                            await fs.WriteAsync(new byte[] { b }, 0, 1, cancellationToken);
                                            arr_r++;
                                            arr_w++;
                                            outsize++;
                                            counter--;
                                        }
                                    }
                                    else
                                    {
                                        arr[arr_w & 0xFFF] = b1;
                                        await fs.WriteAsync(new byte[] { b1 }, 0, 1, cancellationToken);
                                        arr_w++;
                                        outsize++;
                                    }

                                    flag >>= 1;
                                }
                            }
                        }
                    }

                    stream.Position = currentPos;

                    if (fout.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string text = await File.ReadAllTextAsync(fout, Encoding.GetEncoding("shift-jis"), cancellationToken);
                            await File.WriteAllTextAsync(fout, text, Encoding.UTF8, cancellationToken);
                            ExtractionProgress?.Invoke(this, $"{name}(已转换为UTF-8)");
                        }
                        catch
                        {
                        }
                    }

                    if (fout.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                        fout.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using (var image = Image.FromFile(fout))
                            {
                                string pngPath = Path.ChangeExtension(fout, ".png");
                                image.Save(pngPath, ImageFormat.Png);
                                File.Delete(fout);
                                fout = pngPath;
                                ExtractionProgress?.Invoke(this, $"{name}(已转换为png)");
                            }
                        }
                        catch
                        {
                        }
                    }

                    if (!extractedFiles.Contains(fout))
                    {
                        extractedFiles.Add(fout);
                        OnFileExtracted(fout);
                    }
                }
            }
        }

        private async Task ProcessLAC0File(byte[] content, string filePath,
                                 List<string> extractedFiles, CancellationToken cancellationToken)
        {
            using (var stream = new MemoryStream(content))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 4;
                uint nentry = reader.ReadUInt32();

                string fileDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
                string outPath = Path.Combine(fileDirectory, Path.GetFileNameWithoutExtension(filePath));
                Directory.CreateDirectory(outPath);

                ExtractionProgress?.Invoke(this, $"处理LAC文件:{Path.GetFileName(filePath)}, 包含{nentry}个音频文件");

                for (int i = 0; i < nentry; i++)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    stream.Position = 8 + i * 40;

                    byte[] nameBytes = reader.ReadBytes(32);
                    uint size = reader.ReadUInt32();
                    uint offset = reader.ReadUInt32();

                    string name = "";
                    for (int j = 0; j < nameBytes.Length && nameBytes[j] != 0; j++)
                    {
                        name += (char)(255 - nameBytes[j]);
                    }

                    if (string.IsNullOrEmpty(name))
                    {
                        name = $"file_{i:D4}.dat";
                    }

                    name = NormalizeExtension(name);

                    if (offset + size > content.Length)
                    {
                        ExtractionError?.Invoke(this, $"音频文件{name}数据超出范围");
                        continue;
                    }

                    string fout = Path.Combine(outPath, name);
                    ExtractionProgress?.Invoke(this, $"提取音频:{name}");

                    using (var fs = new FileStream(fout, FileMode.Create))
                    {
                        byte[] data = new byte[size];
                        Array.Copy(content, offset, data, 0, size);
                        await fs.WriteAsync(data, 0, data.Length, cancellationToken);
                    }

                    if (!extractedFiles.Contains(fout))
                    {
                        extractedFiles.Add(fout);
                        OnFileExtracted(fout);
                    }
                }
            }
        }

        private string NormalizeExtension(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
                return fileName;

            string lowerExt = extension.ToLowerInvariant();

            if (lowerExt == ".ogg" || lowerExt == ".wav" || lowerExt == ".mp3" ||
                lowerExt == ".flac" || lowerExt == ".aac" || lowerExt == ".m4a")
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                return nameWithoutExt + lowerExt;
            }

            return fileName;
        }

        private string ToShiftJISString(byte[] bytes)
        {
            try
            {
                int length = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] == 0)
                    {
                        length = i;
                        break;
                    }
                }

                if (length == 0)
                    length = bytes.Length;

                return Encoding.GetEncoding("shift-jis").GetString(bytes, 0, length).Trim();
            }
            catch
            {
                int length = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] == 0)
                    {
                        length = i;
                        break;
                    }
                }

                if (length == 0)
                    length = bytes.Length;

                return Encoding.ASCII.GetString(bytes, 0, length).Trim();
            }
        }

        private new void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
        }
    }  
}