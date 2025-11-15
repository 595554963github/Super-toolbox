using System.Collections.Concurrent;
using System.Text;
using Syroot.BinaryData;

namespace super_toolbox
{
    public class NintendoSound_Extractor : BaseExtractor
    {
        private static readonly byte[] CWAV_SIGNATURE = { 0x43, 0x57, 0x41, 0x56, 0xFF, 0xFE }; // "CWAV" + FFFE
        private static readonly byte[] FWAV_SIGNATURE = { 0x46, 0x57, 0x41, 0x56, 0xFE, 0xFF }; // "FWAV" + FEFF
        private static readonly byte[] RWAR_SIGNATURE = { 0x52, 0x57, 0x41, 0x52 }; // "RWAR"
        private static readonly byte[] RWAV_SIGNATURE = { 0x52, 0x57, 0x41, 0x56 }; // "RWAV"

        private const string CWAV_FOLDER = "CWAV";
        private const string FWAV_FOLDER = "FWAV";
        private const string RWAR_FOLDER = "RWAR";
        private const string RWAV_FOLDER = "RWAV";

        private int cwavCounter = 0;
        private int fwavCounter = 0;
        private int rwarCounter = 0;
        private int rwavCounter = 0;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string extractedDir = Path.Combine(directoryPath, "Extracted");
            Directory.CreateDirectory(extractedDir);

            string cwavDir = Path.Combine(extractedDir, CWAV_FOLDER);
            string fwavDir = Path.Combine(extractedDir, FWAV_FOLDER);
            string rwarDir = Path.Combine(extractedDir, RWAR_FOLDER);
            string rwavDir = Path.Combine(extractedDir, RWAV_FOLDER);

            Directory.CreateDirectory(cwavDir);
            Directory.CreateDirectory(fwavDir);
            Directory.CreateDirectory(rwarDir);
            Directory.CreateDirectory(rwavDir);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(extractedDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TotalFilesToExtract = files.Count;

            var extractedFiles = new ConcurrentBag<string>();

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, filePath =>
                    {
                        ThrowIfCancellationRequested(cancellationToken);
                        try
                        {
                            byte[] content = File.ReadAllBytes(filePath);

                            ExtractCwav(filePath, content, cwavDir);
                            ExtractFwav(filePath, content, fwavDir);
                            ExtractRwar(filePath, content, rwarDir);
                            ExtractRwav(filePath, content, rwavDir);
                        }
                        catch (Exception ex)
                        {
                            OnExtractionFailed($"提取文件 {filePath} 时发生错误: {ex.Message}");
                        }
                    });
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("提取操作已取消。");
            }

            sw.Stop();

            int actualExtractedCount = Directory.EnumerateFiles(extractedDir, "*.*", SearchOption.AllDirectories).Count();
            Console.WriteLine($"处理完成，耗时 {sw.Elapsed.TotalSeconds:F2} 秒");
            Console.WriteLine($"共提取出 {actualExtractedCount} 个音频相关文件，统计提取文件数量: {ExtractedFileCount}");
            if (ExtractedFileCount != actualExtractedCount)
            {
                Console.WriteLine("警告: 统计数量与实际数量不符，可能存在文件操作异常。");
            }
        }

        private void ExtractCwav(string filePath, byte[] content, string outputDir)
        {
            int index = 0;

            while (index < content.Length)
            {
                int cwavStartIndex = IndexOf(content, CWAV_SIGNATURE, index);
                if (cwavStartIndex == -1) break;

                int sizeOffset = cwavStartIndex + 0xC;
                if (sizeOffset + 4 > content.Length)
                {
                    index = cwavStartIndex + 1;
                    continue;
                }

                int fileSize = ReadLittleEndianInt32(content, sizeOffset);
                if (fileSize <= 0 || cwavStartIndex + fileSize > content.Length)
                {
                    index = cwavStartIndex + 1;
                    continue;
                }

                byte[] cwavData = new byte[fileSize];
                Array.Copy(content, cwavStartIndex, cwavData, 0, fileSize);

                string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                string cwavFileName = $"{baseFileName}_cwav_{Interlocked.Increment(ref cwavCounter):D4}.bcwav";
                string cwavFilePath = Path.Combine(outputDir, cwavFileName);

                File.WriteAllBytes(cwavFilePath, cwavData);
                OnFileExtracted(cwavFilePath);

                index = cwavStartIndex + fileSize;
            }
        }

        private void ExtractFwav(string filePath, byte[] content, string outputDir)
        {
            int index = 0;

            while (index < content.Length)
            {
                int fwavStartIndex = IndexOf(content, FWAV_SIGNATURE, index);
                if (fwavStartIndex == -1) break;

                int sizeOffset = fwavStartIndex + 0xC;
                if (sizeOffset + 4 > content.Length)
                {
                    index = fwavStartIndex + 1;
                    continue;
                }

                int fileSize = ReadBigEndianInt32(content, sizeOffset);
                if (fileSize <= 0 || fwavStartIndex + fileSize > content.Length)
                {
                    index = fwavStartIndex + 1;
                    continue;
                }

                byte[] fwavData = new byte[fileSize];
                Array.Copy(content, fwavStartIndex, fwavData, 0, fileSize);

                string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                string fwavFileName = $"{baseFileName}_fwav_{Interlocked.Increment(ref fwavCounter):D4}.bfwav";
                string fwavFilePath = Path.Combine(outputDir, fwavFileName);

                File.WriteAllBytes(fwavFilePath, fwavData);
                OnFileExtracted(fwavFilePath);

                index = fwavStartIndex + fileSize;
            }
        }

        private void ExtractRwar(string filePath, byte[] content, string outputDir)
        {
            int index = 0;

            while (index < content.Length)
            {
                int rwarStartIndex = IndexOf(content, RWAR_SIGNATURE, index);
                if (rwarStartIndex == -1) break;

                int sizeOffset = rwarStartIndex + 8;
                if (sizeOffset + 4 > content.Length)
                {
                    index = rwarStartIndex + 1;
                    continue;
                }

                int fileSize = ReadBigEndianInt32(content, sizeOffset);
                if (fileSize <= 0 || rwarStartIndex + fileSize > content.Length)
                {
                    index = rwarStartIndex + 1;
                    continue;
                }

                byte[] rwarData = new byte[fileSize];
                Array.Copy(content, rwarStartIndex, rwarData, 0, fileSize);

                string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                string rwarFileName = $"{baseFileName}_rwar_{Interlocked.Increment(ref rwarCounter):D4}.rwar";
                string rwarFilePath = Path.Combine(outputDir, rwarFileName);

                File.WriteAllBytes(rwarFilePath, rwarData);
                OnFileExtracted(rwarFilePath);

                index = rwarStartIndex + fileSize;
            }
        }

        private void ExtractRwav(string filePath, byte[] content, string outputDir)
        {
            int index = 0;

            while (index < content.Length)
            {
                int rwavStartIndex = IndexOf(content, RWAV_SIGNATURE, index);
                if (rwavStartIndex == -1) break;

                int sizeOffset = rwavStartIndex + 8;
                if (sizeOffset + 4 > content.Length)
                {
                    index = rwavStartIndex + 1;
                    continue;
                }

                int fileSize = ReadBigEndianInt32(content, sizeOffset);
                if (fileSize <= 0 || rwavStartIndex + fileSize > content.Length)
                {
                    index = rwavStartIndex + 1;
                    continue;
                }

                byte[] rwavData = new byte[fileSize];
                Array.Copy(content, rwavStartIndex, rwavData, 0, fileSize);

                string baseFileName = Path.GetFileNameWithoutExtension(filePath);
                string rwavFileName = $"{baseFileName}_rwav_{Interlocked.Increment(ref rwavCounter):D4}.brwav";
                string rwavFilePath = Path.Combine(outputDir, rwavFileName);

                File.WriteAllBytes(rwavFilePath, rwavData);
                OnFileExtracted(rwavFilePath);

                index = rwavStartIndex + fileSize;
            }
        }

        private int ReadBigEndianInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private int ReadLittleEndianInt32(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}