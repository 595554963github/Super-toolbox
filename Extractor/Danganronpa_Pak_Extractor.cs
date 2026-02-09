using System.Text;

namespace super_toolbox
{
    public class Danganronpa_Pak_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ExtractionError?.Invoke(this, $"目录不存在:{directoryPath}");
                OnExtractionFailed($"目录不存在:{directoryPath}");
                return;
            }

            var pakFiles = Directory.GetFiles(directoryPath, "*.pak", SearchOption.AllDirectories);
            if (pakFiles.Length == 0)
            {
                ExtractionError?.Invoke(this, "未找到.pak文件");
                OnExtractionFailed("未找到.pak文件");
                return;
            }

            TotalFilesToExtract = pakFiles.Length;
            ExtractionStarted?.Invoke(this, $"开始处理{pakFiles.Length}个.pak文件");

            try
            {
                await Task.Run(() =>
                {
                    foreach (var pakFile in pakFiles)
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        ExtractionProgress?.Invoke(this, $"正在提取:{Path.GetFileName(pakFile)}");

                        try
                        {
                            int extractedCount = ExtractSinglePak(pakFile, cancellationToken);
                            ExtractionProgress?.Invoke(this, $"提取完成:{Path.GetFileName(pakFile)} ({extractedCount}个文件)");
                        }
                        catch (Exception ex)
                        {
                            ExtractionError?.Invoke(this, $"提取失败:{Path.GetFileName(pakFile)} - {ex.Message}");
                        }
                    }

                    OnExtractionCompleted();
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ExtractionError?.Invoke(this, "提取操作已取消");
                OnExtractionFailed("提取操作已取消");
            }
            catch (Exception ex)
            {
                ExtractionError?.Invoke(this, $"提取过程发生错误:{ex.Message}");
                OnExtractionFailed($"提取过程发生错误:{ex.Message}");
            }
        }

        private int ExtractSinglePak(string pakFilePath, CancellationToken cancellationToken)
        {
            byte[] pakData = File.ReadAllBytes(pakFilePath);

            if (pakData.Length < 8)
            {
                throw new InvalidDataException("PAK文件太小");
            }

            uint fileCount = BitConverter.ToUInt32(pakData, 0);
            uint firstFileOffset = BitConverter.ToUInt32(pakData, 4);

            if (firstFileOffset > pakData.Length)
            {
                throw new InvalidDataException("第一个文件偏移超出文件范围");
            }

            string destDir = Path.Combine(Path.GetDirectoryName(pakFilePath)!, Path.GetFileNameWithoutExtension(pakFilePath));
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            List<uint> fileOffsets = new List<uint>();

            for (int i = 8; i < firstFileOffset; i += 4)
            {
                if (i + 4 > pakData.Length) break;

                bool isFourZero = true;
                for (int j = 0; j < 4; j++)
                {
                    if (pakData[i + j] != 0x00)
                    {
                        isFourZero = false;
                        break;
                    }
                }

                if (isFourZero) break;

                uint offset = BitConverter.ToUInt32(pakData, i);
                if (offset < pakData.Length)
                {
                    fileOffsets.Add(offset);
                }
            }

            fileOffsets.Insert(0, firstFileOffset);
            fileOffsets = fileOffsets.Distinct().OrderBy(o => o).ToList();

            int extractedCount = 0;
            for (int i = 0; i < fileOffsets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint currentOffset = fileOffsets[i];
                uint fileSize = 0;

                if (i < fileOffsets.Count - 1)
                {
                    uint nextOffset = fileOffsets[i + 1];
                    if (nextOffset > currentOffset)
                    {
                        fileSize = nextOffset - currentOffset;
                    }
                }
                else
                {
                    fileSize = (uint)(pakData.Length - currentOffset);
                }

                if (fileSize == 0 || currentOffset + fileSize > pakData.Length) continue;

                byte[] fileData = new byte[fileSize];
                Array.Copy(pakData, (int)currentOffset, fileData, 0, (int)fileSize);

                string extension = GetExtensionFromData(fileData);
                string outputPath = Path.Combine(destDir, $"{Path.GetFileNameWithoutExtension(pakFilePath)}_{i:D4}{extension}");

                File.WriteAllBytes(outputPath, fileData);
                OnFileExtracted(outputPath);
                extractedCount++;
            }

            return extractedCount;
        }

        private string GetExtensionFromData(byte[] data)
        {
            if (data.Length >= 4)
            {
                byte[] headerBytes = new byte[4];
                Array.Copy(data, 0, headerBytes, 0, 4);

                if (headerBytes[0] == 0x4F && headerBytes[1] == 0x4D && headerBytes[2] == 0x47 && headerBytes[3] == 0x2E)
                {
                    return ".gmo";
                }
                else if (headerBytes[0] == 0x4C && headerBytes[1] == 0x4C && headerBytes[2] == 0x46 && headerBytes[3] == 0x53)
                {
                    return ".llfs";
                }
                else if (headerBytes[0] == 0xFC && headerBytes[1] == 0xAA && headerBytes[2] == 0x55 && headerBytes[3] == 0xA7)
                {
                    return ".gxt";
                }
                else
                {
                    return ".bin";
                }
            }
            return ".bin";
        }

        public override void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }
    }
}