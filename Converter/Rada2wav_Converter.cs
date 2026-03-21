using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Rada2wav_Converter : BaseExtractor
    {
        private static string? _tempDllPath;
        private static bool _dllLoaded;

        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        static Rada2wav_Converter()
        {
            try
            {
                _tempDllPath = LoadEmbeddedDll("embedded.RadAudioCodec.dll", "RadAudioCodec_Decode.dll");
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

        [StructLayout(LayoutKind.Sequential)]
        private struct RadAFileHeader
        {
            public uint tag;
            public uint bytes_for_first_decode;
            public byte version;
            public byte channels;
            public ushort rada_header_bytes;
            public byte shift_bits_for_seek_table_samples;
            public byte bits_for_seek_table_bytes;
            public byte bits_for_seek_table_samples;
            public byte sample_rate;
            public ulong frame_count;
            public ulong file_size;
            public ushort seek_table_entry_count;
            public ushort max_block_size;
            public ushort padding0;
            public ushort padding1;
        }

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint RadAudioGetBuildVersion();

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int RadAudioGetMemoryNeededToOpen(byte[] fileData, int fileDataLen, out uint outMemoryRequired);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int RadAudioOpenDecoder(byte[] fileData, int fileDataLen, IntPtr container, int containerBytes);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint RadAudioGetSampleRate(byte[] fileData, int fileDataLen);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte RadAudioGetChannels(byte[] fileData, int fileDataLen);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong RadAudioGetFrameCount(byte[] fileData, int fileDataLen);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint RadAudioGetMaxBlockSize(byte[] fileData, int fileDataLen);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int RadAudioExamineBlock(IntPtr container, byte[] buffer, int bufferLen, out uint neededBytes);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RadAudioNotifySeek(IntPtr container);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern short RadAudioDecodeBlock(IntPtr container, byte[] buffer, int bufferLen, float[] outputBuffer, int outputStride, out int consumedBytes);

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RadAudioFreeDecoderContainer(IntPtr container);

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!_dllLoaded)
            {
                ConversionError?.Invoke(this, "无法加载RadAudioCodec.dll");
                OnConversionFailed("无法加载RadAudioCodec.dll");
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                ConversionError?.Invoke(this, $"源文件夹{directoryPath}不存在");
                OnConversionFailed($"源文件夹{directoryPath}不存在");
                return;
            }

            ConversionStarted?.Invoke(this, $"开始处理目录:{directoryPath}");

            var radaFiles = Directory.GetFiles(directoryPath, "*.rada", SearchOption.AllDirectories)
                .OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    var match = Regex.Match(fileName, @"_(\d+)$");
                    return match.Success && int.TryParse(match.Groups[1].Value, out int num) ? num : int.MaxValue;
                })
                .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            TotalFilesToConvert = radaFiles.Length;
            int successCount = 0;

            try
            {
                foreach (var radaFilePath in radaFiles)
                {
                    ThrowIfCancellationRequested(cancellationToken);

                    string fileName = Path.GetFileNameWithoutExtension(radaFilePath);
                    ConversionProgress?.Invoke(this, $"正在处理:{fileName}.rada");

                    string fileDirectory = Path.GetDirectoryName(radaFilePath) ?? string.Empty;
                    string wavFilePath = Path.Combine(fileDirectory, $"{fileName}.wav");

                    try
                    {
                        if (File.Exists(wavFilePath))
                            File.Delete(wavFilePath);

                        bool conversionSuccess = await Task.Run(() => ConvertRadaToWav(radaFilePath, wavFilePath, cancellationToken));

                        if (conversionSuccess && File.Exists(wavFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(wavFilePath)}");
                            OnFileConverted(wavFilePath);
                        }
                        else
                        {
                            ConversionError?.Invoke(this, $"{fileName}.rada转换失败");
                            OnConversionFailed($"{fileName}.rada转换失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        ConversionError?.Invoke(this, $"转换异常:{ex.Message}");
                        OnConversionFailed($"{fileName}.rada处理错误:{ex.Message}");
                    }
                }

                ConversionProgress?.Invoke(this, successCount > 0 ?
                    $"转换完成,成功转换{successCount}/{TotalFilesToConvert}个文件" :
                    "转换完成,但未成功转换任何文件");

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

        private bool ConvertRadaToWav(string radaFilePath, string wavFilePath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(radaFilePath);

                uint sampleRate = RadAudioGetSampleRate(fileData, fileData.Length);
                byte channels = RadAudioGetChannels(fileData, fileData.Length);
                ulong frameCount = RadAudioGetFrameCount(fileData, fileData.Length);

                if (sampleRate == 0 || channels == 0 || frameCount == 0)
                    throw new InvalidOperationException("无效的RADA文件");

                uint neededMemory;
                int result = RadAudioGetMemoryNeededToOpen(fileData, fileData.Length, out neededMemory);
                if (result != 0)
                    throw new InvalidOperationException("获取解码器内存需求失败");

                IntPtr container = Marshal.AllocHGlobal((int)neededMemory);
                try
                {
                    result = RadAudioOpenDecoder(fileData, fileData.Length, container, (int)neededMemory);
                    if (result != 1)
                        throw new InvalidOperationException("打开解码器失败");

                    RadAudioNotifySeek(container);

                    using (var ms = new MemoryStream())
                    {
                        uint posUint = RadAudioGetBytesToOpen(fileData, fileData.Length);
                        if (posUint == 0)
                            throw new InvalidOperationException("无法定位数据起始位置");

                        int pos = (int)posUint;
                        int remaining = fileData.Length - pos;

                        while (remaining > 0)
                        {
                            int blockSize = Math.Min(8192, remaining);
                            byte[] blockBuffer = new byte[blockSize];
                            Array.Copy(fileData, pos, blockBuffer, 0, blockSize);

                            uint neededBytes;
                            int examineResult = RadAudioExamineBlock(container, blockBuffer, blockBuffer.Length, out neededBytes);

                            if (examineResult == 0)
                            {
                                if (neededBytes > blockBuffer.Length && remaining >= neededBytes)
                                {
                                    blockBuffer = new byte[neededBytes];
                                    Array.Copy(fileData, pos, blockBuffer, 0, (int)neededBytes);
                                    examineResult = RadAudioExamineBlock(container, blockBuffer, blockBuffer.Length, out neededBytes);
                                }
                                else
                                {
                                    break;
                                }
                            }

                            if (examineResult != 1)
                                break;

                            int maxSamples = 2048;
                            float[] outputBuffer = new float[maxSamples * channels];
                            int consumedBytes;

                            short decodedSamples = RadAudioDecodeBlock(container, blockBuffer, blockBuffer.Length, outputBuffer, maxSamples, out consumedBytes);

                            if (decodedSamples == -2 || decodedSamples == -1)
                                break;

                            if (decodedSamples > 0)
                            {
                                short[] pcmBuffer = new short[decodedSamples * channels];

                                for (int frame = 0; frame < decodedSamples; frame++)
                                {
                                    for (int ch = 0; ch < channels; ch++)
                                    {
                                        float sample = outputBuffer[ch * maxSamples + frame];

                                        if (sample > 1.0f) sample = 1.0f;
                                        if (sample < -1.0f) sample = -1.0f;

                                        pcmBuffer[frame * channels + ch] = (short)(sample * 32767.0f);
                                    }
                                }

                                byte[] pcmBytes = new byte[pcmBuffer.Length * 2];
                                Buffer.BlockCopy(pcmBuffer, 0, pcmBytes, 0, pcmBytes.Length);
                                ms.Write(pcmBytes, 0, pcmBytes.Length);
                            }

                            pos += consumedBytes;
                            remaining = fileData.Length - pos;
                        }

                        byte[] pcmData = ms.ToArray();
                        if (pcmData.Length == 0)
                            throw new Exception("没有解码出任何音频数据");

                        WriteWavFile(wavFilePath, pcmData, (int)sampleRate, channels, 16);
                    }
                }
                finally
                {
                    RadAudioFreeDecoderContainer(container);
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

        [DllImport("RadAudioCodec_Decode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint RadAudioGetBytesToOpen(byte[] fileData, int fileDataLen);

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