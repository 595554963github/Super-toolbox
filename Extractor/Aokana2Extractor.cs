using System.Text;

namespace super_toolbox
{
    public class Aokana2Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            List<string> allExtractedFileNames = new List<string>();

            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"目录不存在:{directoryPath}");
                OnExtractionFailed($"目录不存在:{directoryPath}");
                return;
            }

            ExtractionStarted?.Invoke(this, $"开始从目录{directoryPath}提取Aokana2 DAT文件");

            try
            {
                var datFiles = Directory.GetFiles(directoryPath, "*.dat", SearchOption.AllDirectories);
                int totalFilesToExtract = 0;
                foreach (var datFile in datFiles)
                {
                    try
                    {
                        using var pread = new PRead(datFile);
                        totalFilesToExtract += pread.FileTable.Count;
                    }
                    catch { }
                }
                TotalFilesToExtract = totalFilesToExtract;

                foreach (var datFile in datFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    ExtractionProgress?.Invoke(this, $"处理文件:{Path.GetFileName(datFile)}");

                    try
                    {
                        var extractedFiles = await ExtractFromDatFileAsync(datFile, directoryPath, cancellationToken);
                        allExtractedFileNames.AddRange(extractedFiles);
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"处理文件{datFile}时出错:{ex.Message}");
                        OnExtractionFailed($"处理文件{datFile} 时出错:{ex.Message}");
                    }
                }

                ExtractionProgress?.Invoke(this, $"提取完成,共提取{allExtractedFileNames.Count}个文件");
                OnExtractionCompleted();
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程中发生错误:{ex.Message}");
                OnExtractionFailed($"提取过程中发生错误:{ex.Message}");
            }
        }

        private async Task<List<string>> ExtractFromDatFileAsync(string datFile, string outputDir, CancellationToken cancellationToken)
        {
            List<string> extractedFiles = new List<string>();
            var pread = new PRead(datFile);

            try
            {
                if (pread == null)
                {
                    ExtractionError?.Invoke(this, $"无法读取文件:{datFile}");
                    return extractedFiles;
                }

                string datFileName = Path.GetFileNameWithoutExtension(datFile);
                string datOutputDir = Path.Combine(outputDir, "Extracted", datFileName);
                Directory.CreateDirectory(datOutputDir);

                foreach (var filename in pread.FileTable.Keys)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    var all_bytes = pread.Data(filename);
                    if (all_bytes == null) continue;

                    string save_path = Path.Combine(datOutputDir, filename);

                    try
                    {
                        var directoryPath = Path.GetDirectoryName(save_path);
                        if (directoryPath != null)
                        {
                            Directory.CreateDirectory(directoryPath);
                        }
                        await File.WriteAllBytesAsync(save_path, all_bytes, cancellationToken);
                        extractedFiles.Add(save_path);
                        OnFileExtracted(save_path);
                    }
                    catch (Exception ex)
                    {
                        ExtractionError?.Invoke(this, $"保存文件{filename}时出错:{ex.Message}");
                    }
                }
            }
            finally
            {
                pread?.Release();
            }
            return extractedFiles;
        }
    }

    public class PRead : IDisposable
    {
        public PRead(string fn)
        {
            this.fs = new FileStream(fn, FileMode.Open, FileAccess.Read);
            this.FileTable = new Dictionary<string, PRead.FileEntry>();
            this.Init();
            if (fn.ToLower().EndsWith("adult.dat"))
            {
                this.FileTable.Remove("def/version.txt");
            }
        }

        public void Release()
        {
            if (this.fs != null)
            {
                this.fs.Close();
                this.fs.Dispose();
                this.fs = null!;
            }
        }

        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }

        ~PRead()
        {
            Release();
        }

        private void Init()
        {
            this.FileTable = new Dictionary<string, PRead.FileEntry>();
            this.fs.Position = 0L;
            byte[] array = new byte[1024];
            this.fs.Read(array, 0, 1024);
            int num = 0;
            for (int i = 3; i < 255; i++)
            {
                num += BitConverter.ToInt32(array, i * 4);
            }
            byte[] array2 = new byte[16 * num];
            this.fs.Read(array2, 0, array2.Length);
            this.DecryptData(array2, 16 * num, BitConverter.ToUInt32(array, 212));
            int num2 = BitConverter.ToInt32(array2, 12) - (1024 + 16 * num);
            byte[] array3 = new byte[num2];
            this.fs.Read(array3, 0, num2);
            this.DecryptData(array3, num2, BitConverter.ToUInt32(array, 92));
            this.InitFileTable(array2, array3, num);
        }

        protected void InitFileTable(byte[] rtoc, byte[] rpaths, int numfiles)
        {
            int num = 0;
            for (int i = 0; i < numfiles; i++)
            {
                int num2 = 16 * i;
                uint length = BitConverter.ToUInt32(rtoc, num2);
                int nameOffset = BitConverter.ToInt32(rtoc, num2 + 4);
                uint key = BitConverter.ToUInt32(rtoc, num2 + 8);
                uint position = BitConverter.ToUInt32(rtoc, num2 + 12);
                int nameEnd = nameOffset;
                while (nameEnd < rpaths.Length && rpaths[nameEnd] != 0)
                {
                    nameEnd++;
                }
                string fileName = Encoding.ASCII.GetString(rpaths, num, nameEnd - num).ToLower();
                PRead.FileEntry entry = default(PRead.FileEntry);
                entry.Position = position;
                entry.Length = length;
                entry.Key = key;
                this.FileTable.Add(fileName, entry);
                num = nameEnd + 1;
            }
        }

        private void GenerateKey(byte[] b, uint k0)
        {
            uint num = k0 * 4892U + 42816U;
            uint num2 = num << 7 ^ num;
            for (int i = 0; i < 256; i++)
            {
                num -= k0;
                num += num2;
                num2 = num + 156U;
                num *= (num2 & 206U);
                b[i] = (byte)num;
                num >>= 3;
            }
        }

        protected void DecryptData(byte[] b, int length, uint key)
        {
            byte[] keyArray = new byte[256];
            this.GenerateKey(keyArray, key);
            for (int i = 0; i < length; i++)
            {
                byte b2 = b[i];
                b2 ^= keyArray[i % 179];
                b2 += 3;
                b2 += keyArray[i % 89];
                b2 ^= 119;
                b[i] = b2;
            }
        }

        public virtual byte[]? Data(string fn)
        {
            PRead.FileEntry entry;
            if (!this.FileTable.TryGetValue(fn, out entry))
            {
                return null;
            }
            this.fs.Position = (long)((ulong)entry.Position);
            byte[] array = new byte[entry.Length];
            this.fs.Read(array, 0, array.Length);
            this.DecryptData(array, array.Length, entry.Key);
            return array;
        }

        private FileStream fs;
        public Dictionary<string, PRead.FileEntry> FileTable;

        public struct FileEntry
        {
            public uint Position;
            public uint Length;
            public uint Key;
        }
    }
}
