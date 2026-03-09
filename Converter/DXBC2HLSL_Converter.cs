using System.Runtime.InteropServices;

namespace super_toolbox
{
    public class DXBC2HLSL_Converter : BaseExtractor
    {
        public new event EventHandler<string>? ConversionStarted;
        public new event EventHandler<string>? ConversionProgress;
        public new event EventHandler<string>? ConversionError;

        private const int BUFFER_SIZE = 1024 * 1024 * 4;
        private static readonly byte[] DXBC_MAGIC_HEADER = new byte[] { 0x44, 0x58, 0x42, 0x43 };

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("HLSLDecompiler.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int DecompileDXBCFromFile(string filename, byte[] outputBuffer, int bufferSize, byte[] modelBuffer, int modelSize);

        [DllImport("HLSLDecompiler.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern void SetDebugOutput(int enable);

        private static string _dllPath;

        static DXBC2HLSL_Converter()
        {
            _dllPath = Path.Combine(Path.GetTempPath(), "HLSLDecompiler.dll");
            ExtractDll();
            LoadLibrary(_dllPath);
            SetDebugOutput(0);
        }
        private static void ExtractDll()
        {
            if (File.Exists(_dllPath)) return;

            using (var stream = typeof(DXBC2HLSL_Converter).Assembly.GetManifestResourceStream("embedded.HLSLDecompiler.dll"))
            {
                if (stream == null) throw new Exception("嵌入的DLL不存在");
                using (var fileStream = new FileStream(_dllPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }
        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var dxbcFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => IsDXBCFile(f))
                .ToArray();

            TotalFilesToConvert = dxbcFiles.Length;
            int successCount = 0;

            foreach (var dxbcFilePath in dxbcFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileNameWithoutExtension(dxbcFilePath);
                ConversionProgress?.Invoke(this, $"正在处理:{fileName}");

                string fileDirectory = Path.GetDirectoryName(dxbcFilePath) ?? string.Empty;
                string hlslFile = Path.Combine(fileDirectory, $"{fileName}.hlsl");

                bool success = await ConvertDXBCToHLSL(dxbcFilePath, hlslFile);
                if (success)
                {
                    if (File.Exists(hlslFile))
                    {
                        successCount++;
                        ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(hlslFile)}");
                        OnFileConverted(hlslFile);
                    }
                }
                else
                {
                    ConversionError?.Invoke(this, $"{fileName}转换失败");
                }
            }

            ConversionProgress?.Invoke(this, $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件");
        }

        private bool IsDXBCFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 4) return false;
                    byte[] header = new byte[4];
                    fs.Read(header, 0, 4);
                    return header.SequenceEqual(DXBC_MAGIC_HEADER);
                }
            }
            catch
            {
                return false;
            }
        }

        private Task<bool> ConvertDXBCToHLSL(string dxbcFilePath, string outputFilePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    byte[] outputBuffer = new byte[BUFFER_SIZE];
                    byte[] modelBuffer = new byte[64];

                    int result = DecompileDXBCFromFile(dxbcFilePath, outputBuffer, BUFFER_SIZE, modelBuffer, 64);
                    if (result != 0) return false;

                    string hlslContent = System.Text.Encoding.UTF8.GetString(outputBuffer).TrimEnd('\0');
                    if (string.IsNullOrEmpty(hlslContent)) return false;

                    File.WriteAllText(outputFilePath, hlslContent);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
