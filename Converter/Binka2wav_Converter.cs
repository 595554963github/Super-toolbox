using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Binka2wav_Converter : BaseExtractor
    {
        private static string? _tempDllPath;
        private static bool _dllLoaded;

        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        static Binka2wav_Converter()
        {
            try
            {
                _tempDllPath = LoadEmbeddedDll("embedded.BinkAudioCodec.dll", "BinkAudioCodec_Decode.dll");
                _dllLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载DLL失败:{ex.Message}");
                _dllLoaded = false;
            }
        }

        private new static string LoadEmbeddedDll(string resourceName, string outputFileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string tempPath = Path.Combine(Path.GetTempPath(), outputFileName);

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException($"嵌入式资源{resourceName}未找到");

                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }

            LoadLibrary(tempPath);
            return tempPath;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BinkAudioFileHeader
        {
            public uint tag;
            public byte version;
            public byte channels;
            public ushort padding;
            public uint rate;
            public uint sample_count;
            public ushort max_comp_space_needed;
            public ushort flags;
            public uint output_file_size;
            public ushort seek_table_entry_count;
            public ushort blocks_per_seek_table_entry;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UEBinkAudioDecodeInterface
        {
            public IntPtr MemoryFn;
            public IntPtr OpenFn;
            public IntPtr DecodeFn;
            public IntPtr ResetStartFn;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint DecodeMemoryFn(uint rate, uint chans);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint DecodeOpenFn(IntPtr decoderMem, uint rate, uint chans, bool interleaveOutput, bool isBinkAudio2);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint DecodeFn(IntPtr decoderMem, byte[] outputBuffer, uint outputBufferLen, ref IntPtr inputBuffer, IntPtr inputBufferEnd);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DecodeResetStartFrameFn(IntPtr decoderMem);

        [DllImport("BinkAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetBinkAudioDecodeInterface();

        private static IntPtr _interfacePtr = IntPtr.Zero;
        private static UEBinkAudioDecodeInterface _interface;

        private static void EnsureInterfaceLoaded()
        {
            if (_interfacePtr == IntPtr.Zero)
            {
                _interfacePtr = GetBinkAudioDecodeInterface();
                if (_interfacePtr != IntPtr.Zero)
                {
                    _interface = (UEBinkAudioDecodeInterface)Marshal.PtrToStructure(_interfacePtr, typeof(UEBinkAudioDecodeInterface))!;
                }
            }
        }

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!_dllLoaded)
            {
                ConversionError?.Invoke(this, "无法加载BinkAudioCodec.dll");
                OnConversionFailed("无法加载BinkAudioCodec.dll");
                return;
            }

            EnsureInterfaceLoaded();

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var binkaFiles = Directory.GetFiles(directoryPath, "*.binka", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    return match.Success && int.TryParse(match.Groups[1].Value, out int num) ? num : int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = binkaFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var binkaFilePath in binkaFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(binkaFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.binka");

                    string fileDirectory = Path.GetDirectoryName(binkaFilePath) ?? string.Empty;
                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavFilePath))
                            File.Delete(wavFilePath);

                        bool conversionSuccess = await Task.Run(() =>
                            ConvertBinkaToWav(binkaFilePath, wavFilePath, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFilePath)}");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.binka转换失败");
                            OnConversionFailed($"{fileName}.binka转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.binka处理错误:{ex.Message}");
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

        private bool ConvertBinkaToWav(string binkaFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(binkaFilePath);

                int headerSize = Marshal.SizeOf(typeof(BinkAudioFileHeader));
                if (fileData.Length < headerSize)
                    throw new InvalidOperationException("无效的BINKA文件");

                BinkAudioFileHeader header;
                IntPtr headerPtr = Marshal.AllocHGlobal(headerSize);
                try
                {
                    Marshal.Copy(fileData, 0, headerPtr, headerSize);
                    header = (BinkAudioFileHeader)Marshal.PtrToStructure(headerPtr, typeof(BinkAudioFileHeader))!;
                }
                finally
                {
                    Marshal.FreeHGlobal(headerPtr);
                }

                if (header.tag != 0x55454241)
                    throw new InvalidOperationException($"无效的文件标识:0x{header.tag:X8}");

                int dataOffset = headerSize + header.seek_table_entry_count * sizeof(ushort);
                if (dataOffset >= fileData.Length)
                    throw new InvalidOperationException("文件数据不完整");

                DecodeMemoryFn memoryFn = (DecodeMemoryFn)Marshal.GetDelegateForFunctionPointer(_interface.MemoryFn, typeof(DecodeMemoryFn));
                DecodeOpenFn openFn = (DecodeOpenFn)Marshal.GetDelegateForFunctionPointer(_interface.OpenFn, typeof(DecodeOpenFn));
                DecodeFn decodeFn = (DecodeFn)Marshal.GetDelegateForFunctionPointer(_interface.DecodeFn, typeof(DecodeFn));
                DecodeResetStartFrameFn resetStartFn = (DecodeResetStartFrameFn)Marshal.GetDelegateForFunctionPointer(_interface.ResetStartFn, typeof(DecodeResetStartFrameFn));

                uint decoderMemSize = memoryFn(header.rate, header.channels);
                IntPtr decoderMem = Marshal.AllocHGlobal((int)decoderMemSize);

                try
                {
                    uint result = openFn(decoderMem, header.rate, header.channels, true, true);
                    if (result == 0)
                        throw new Exception("解码器初始化失败");

                    resetStartFn(decoderMem);

                    using (var ms = new MemoryStream())
                    {
                        int pos = dataOffset;
                        int remaining = fileData.Length - dataOffset;

                        while (remaining >= 4)
                        {
                            uint blockHeader = BitConverter.ToUInt32(fileData, pos);
                            if ((blockHeader & 0xFFFF) != 0x9999)
                                throw new Exception($"无效的块头:0x{blockHeader:X8}");

                            uint frameSize = blockHeader >> 16;
                            int headerBytes = 4;

                            if (frameSize == 0xFFFF)
                            {
                                if (remaining < 8)
                                    break;

                                uint trimHeader = BitConverter.ToUInt32(fileData, pos + 4);
                                frameSize = trimHeader & 0xFFFF;
                                headerBytes = 8;
                            }

                            if (frameSize == 0 || frameSize > remaining - headerBytes)
                                break;

                            byte[] blockData = new byte[frameSize];
                            Array.Copy(fileData, pos + headerBytes, blockData, 0, (int)frameSize);

                            IntPtr inputPtr = Marshal.AllocHGlobal(blockData.Length + 72);
                            try
                            {
                                Marshal.Copy(blockData, 0, inputPtr, blockData.Length);
                                IntPtr inputBuffer = inputPtr;
                                IntPtr inputBufferEnd = inputPtr + blockData.Length;

                                while (inputBuffer < inputBufferEnd)
                                {
                                    byte[] outputBuffer = new byte[2048 * 2 * 2];
                                    uint outputLen = decodeFn(decoderMem, outputBuffer, (uint)outputBuffer.Length, ref inputBuffer, inputBufferEnd);

                                    if (outputLen == 0)
                                        break;

                                    ms.Write(outputBuffer, 0, (int)outputLen);
                                }
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(inputPtr);
                            }

                            pos += headerBytes + (int)frameSize;
                            remaining = fileData.Length - pos;
                        }

                        byte[] pcmData = ms.ToArray();
                        if (pcmData.Length == 0)
                            throw new Exception("没有解码出任何音频数据");

                        WriteWavFile(wavFilePath, pcmData, (int)header.rate, header.channels, 16);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(decoderMem);
                }

                return true;
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

        private void WriteWavFile(string filePath, byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
        {
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            int blockAlign = channels * bitsPerSample / 8;
            int dataSize = pcmData.Length;
            int fileSize = 36 + dataSize;

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(fileSize);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((short)blockAlign);
                writer.Write((short)bitsPerSample);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);
                writer.Write(pcmData);
            }
        }
    }
}