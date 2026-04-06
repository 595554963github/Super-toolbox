using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2w64_Converter : BaseExtractor
    {
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var wavFiles = Directory.GetFiles(directoryPath, "*.wav", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                        return num;
                    return int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = wavFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var wavFilePath in wavFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(wavFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.wav");

                    string fileDirectory = Path.GetDirectoryName(wavFilePath) ?? string.Empty;
                    string w64File = Path.Combine(fileDirectory, $"{fileName}.w64");

                    try
                    {
                        if (File.Exists(w64File))
                        {
                            File.Delete(w64File);
                        }

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertWavToW64(wavFilePath, w64File, cancellationToken));

                        if (conversionSuccess && File.Exists(w64File))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(w64File)}");
                            OnFileConverted(w64File);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.wav转换失败");
                            OnConversionFailed($"{fileName}.wav转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.wav处理错误:{ex.Message}");
                    }
                }

                if (successCount > 0)
                {
                    ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
                }
                else
                {
                    ConversionProgress?.Invoke(this, "转换完成,但未成功转换任何文件");
                }

                OnConversionCompleted();
            }
            catch (OperationCanceledException)
            {
                ConversionError?.Invoke(this, "操作已取消");
                OnConversionFailed("操作已取消");
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"严重错误:{ex.Message}");
                OnConversionFailed($"严重错误:{ex.Message}");
            }
        }

        private bool ConvertWavToW64(string wavFilePath, string w64FilePath, CancellationToken cancellationToken)
        {
            try
            {
                using (FileStream input = new FileStream(wavFilePath, FileMode.Open, FileAccess.Read))
                using (FileStream output = new FileStream(w64FilePath, FileMode.Create, FileAccess.Write))
                using (BinaryReader reader = new BinaryReader(input))
                using (BinaryWriter writer = new BinaryWriter(output))
                {
                    long wavFileSize = input.Length;
                    long w64RealSize = wavFileSize + 60;

                    reader.BaseStream.Seek(0, SeekOrigin.Begin);
                    if (new string(reader.ReadChars(4)) != "RIFF")
                        return false;

                    reader.BaseStream.Seek(8, SeekOrigin.Begin);
                    if (new string(reader.ReadChars(4)) != "WAVE")
                        return false;

                    reader.BaseStream.Seek(12, SeekOrigin.Begin);
                    byte[] fmtData = Array.Empty<byte>();
                    long dataOffset = 0;
                    long dataSize = 0;

                    while (reader.BaseStream.Position < reader.BaseStream.Length - 8)
                    {
                        string chunkId = new string(reader.ReadChars(4));
                        int chunkSize = reader.ReadInt32();

                        if (chunkId == "fmt ")
                        {
                            fmtData = reader.ReadBytes(chunkSize);
                            if (chunkSize % 2 == 1)
                                reader.ReadByte();
                        }
                        else if (chunkId == "data")
                        {
                            dataOffset = reader.BaseStream.Position;
                            dataSize = chunkSize;
                            break;
                        }
                        else
                        {
                            reader.BaseStream.Seek(chunkSize + (chunkSize % 2), SeekOrigin.Current);
                        }
                    }

                    if (fmtData == null || dataSize == 0)
                        return false;

                    long dataChunkSize = w64RealSize - 0x50;

                    byte[] riffGuid = new byte[]
                    {
                        0x72, 0x69, 0x66, 0x66, 0x2E, 0x91, 0xCF, 0x11,
                        0xA5, 0xD6, 0x28, 0xDB, 0x04, 0xC1, 0x00, 0x00
                    };

                    byte[] waveGuid = new byte[]
                    {
                        0x77, 0x61, 0x76, 0x65, 0xF3, 0xAC, 0xD3, 0x11,
                        0x8C, 0xD1, 0x00, 0xC0, 0x4F, 0x8E, 0xDB, 0x8A
                    };

                    byte[] fmtGuid = new byte[]
                    {
                        0x66, 0x6D, 0x74, 0x20, 0xF3, 0xAC, 0xD3, 0x11,
                        0x8C, 0xD1, 0x00, 0xC0, 0x4F, 0x8E, 0xDB, 0x8A
                    };

                    byte[] dataGuid = new byte[]
                    {
                        0x64, 0x61, 0x74, 0x61, 0xF3, 0xAC, 0xD3, 0x11,
                        0x8C, 0xD1, 0x00, 0xC0, 0x4F, 0x8E, 0xDB, 0x8A
                    };

                    writer.Write(riffGuid);
                    writer.Write(w64RealSize);
                    writer.Write(waveGuid);

                    writer.Write(fmtGuid);
                    writer.Write(40L);

                    byte[] combined = new byte[32];
                    Buffer.BlockCopy(fmtData, 0, combined, 0, fmtData.Length);
                    Buffer.BlockCopy(dataGuid, 0, combined, fmtData.Length, dataGuid.Length);
                    writer.Write(combined);

                    writer.Write(dataChunkSize);

                    reader.BaseStream.Seek(dataOffset, SeekOrigin.Begin);
                    byte[] buffer = new byte[65536];
                    int bytesRead;
                    while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                        writer.Write(buffer, 0, bytesRead);

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ConversionError?.Invoke(this, $"转换错误:{ex.Message}");
                return false;
            }
        }
    }
}