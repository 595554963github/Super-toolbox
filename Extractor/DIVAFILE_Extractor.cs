using System.Text;
using System.Security.Cryptography;

namespace super_toolbox
{
    public class DIVAFILE_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

        private static readonly byte[] DIVA_MAGIC = Encoding.ASCII.GetBytes("DIVAFILE");
        private static readonly byte[] DIVA_KEY = Encoding.ASCII.GetBytes("file access deny");
        private const int HEADER_SIZE = 16;

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

            var allFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                                   .Where(f => new FileInfo(f).Length >= HEADER_SIZE);

            TotalFilesToExtract = allFiles.Count();
            int processedFiles = 0;

            foreach (var file in allFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);

                try
                {
                    byte[] content = await File.ReadAllBytesAsync(file, cancellationToken);

                    if (content.Length >= HEADER_SIZE)
                    {
                        byte[] magic = new byte[8];
                        Array.Copy(content, 0, magic, 0, 8);

                        if (magic.SequenceEqual(DIVA_MAGIC))
                        {
                            ExtractionProgress?.Invoke(this, $"正在处理DIVAFILE文件:{Path.GetFileName(file)}");

                            string outputDir = Path.Combine(Path.GetDirectoryName(file) ?? directoryPath,
                                                           $"{Path.GetFileNameWithoutExtension(file)}_decrypted");
                            Directory.CreateDirectory(outputDir);

                            await ProcessDIVAFILE(content, Path.GetFileNameWithoutExtension(file), outputDir, extractedFiles, cancellationToken);
                        }
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
                    OnExtractionFailed($"处理文件{file}时出错:{e.Message}");
                }

                processedFiles++;
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成,共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成,未找到DIVAFILE文件");
            }
            OnExtractionCompleted();
        }

        private async Task ProcessDIVAFILE(byte[] content, string baseFileName, string outputDir,
                                         List<string> extractedFiles, CancellationToken cancellationToken)
        {
            if (content.Length < HEADER_SIZE)
            {
                throw new InvalidDataException("文件太小，不是有效的DIVAFILE");
            }

            int lenPayload = BitConverter.ToInt32(content, 8);
            int lenPlaintext = BitConverter.ToInt32(content, 12);

            if (lenPayload <= 0 || lenPlaintext <= 0 || lenPlaintext > lenPayload)
            {
                throw new InvalidDataException("无效的长度字段");
            }

            if (content.Length < HEADER_SIZE + lenPayload)
            {
                throw new InvalidDataException("文件数据不完整");
            }

            byte[] encryptedPayload = new byte[lenPayload];
            Array.Copy(content, HEADER_SIZE, encryptedPayload, 0, lenPayload);

            byte[] decryptedPayload = DecryptAES(encryptedPayload);

            string extension = GetExtensionFromHeader(decryptedPayload);
            string fileName = $"{baseFileName}.{extension}";
            string outputPath = Path.Combine(outputDir, fileName);
            outputPath = GetUniqueFilePath(outputPath);

            byte[] plaintextData = new byte[lenPlaintext];
            Array.Copy(decryptedPayload, 0, plaintextData, 0, lenPlaintext);

            await File.WriteAllBytesAsync(outputPath, plaintextData, cancellationToken);
            extractedFiles.Add(outputPath);
            OnFileExtracted(outputPath);
            ExtractionProgress?.Invoke(this, $"已解密:{Path.GetFileName(outputPath)}");
        }

        private byte[] DecryptAES(byte[] encryptedData)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = DIVA_KEY;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.BlockSize = 128;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
                }
            }
        }

        private string GetExtensionFromHeader(byte[] fileData)
        {
            if (fileData == null || fileData.Length < 3)
                return "dat";

            StringBuilder headerBuilder = new StringBuilder();

            for (int i = 0; i < Math.Min(4, fileData.Length); i++)
            {
                byte b = fileData[i];
                if (b >= 33 && b <= 126)
                {
                    headerBuilder.Append((char)b);
                }
                else
                {
                    break;
                }
            }

            string header = headerBuilder.ToString();

            if (header.Length >= 2)
            {
                string cleanHeader = new string(header.Where(c => char.IsLetter(c)).ToArray());

                if (cleanHeader.Length >= 2)
                    return cleanHeader.ToLower();
            }

            return "dat";
        }

        private string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return filePath;
            }

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string fileExtension = Path.GetExtension(filePath);

            int duplicateCount = 1;
            string newFilePath;
            do
            {
                newFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{duplicateCount}{fileExtension}");
                duplicateCount++;
            } while (File.Exists(newFilePath));

            return newFilePath;
        }
    }
}