using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2binka1_Converter : BaseExtractor
    {
        private static string? _tempDllPath;
        private static bool _dllLoaded;

        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        static Wav2binka1_Converter()
        {
            try
            {
                _tempDllPath = LoadEmbeddedDll("embedded.BinkAudioCodec.dll", "BinkAudioCodec_Encode.dll");
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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AllocDelegate(UIntPtr bytes);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreeDelegate(IntPtr ptr);

        [DllImport("BinkAudioCodec_Encode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte CompressBinkAudio(IntPtr pcmData, uint pcmDataLen, uint pcmRate, byte pcmChannels, byte quality, byte generateSeekTable, ushort seekTableMaxEntries, AllocDelegate memAlloc, FreeDelegate memFree, out IntPtr outData, out uint outDataLen);

        private static IntPtr AllocMemory(UIntPtr bytes) => Marshal.AllocHGlobal((int)bytes.ToUInt64());

        private static void FreeMemory(IntPtr ptr) => Marshal.FreeHGlobal(ptr);

        private static readonly AllocDelegate _allocDelegate = AllocMemory;
        private static readonly FreeDelegate _freeDelegate = FreeMemory;

        public override async Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (!_dllLoaded)
            {
                ConversionError?.Invoke(this, "无法加载BinkAudioCodec.dll");
                OnConversionFailed("无法加载BinkAudioCodec.dll");
                return;
            }

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
                    return match.Success && int.TryParse(match.Groups[1].Value, out int num) ? num : int.MaxValue;
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
                    string binkaFilePath = Path.Combine(fileDirectory, $"{fileName}.binka");

                    try
                    {
                        if (File.Exists(binkaFilePath)) File.Delete(binkaFilePath);

                        bool conversionSuccess = await Task.Run(() => ConvertWavToBinka(wavFilePath, binkaFilePath, cancellationToken));

                        if (conversionSuccess && File.Exists(binkaFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(binkaFilePath)}");
                            OnFileConverted(binkaFilePath);
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

        private bool ConvertWavToBinka(string wavFilePath, string binkaFilePath, CancellationToken cancellationToken)
        {
            try
            {
                byte[] wavData = File.ReadAllBytes(wavFilePath);

                if (wavData.Length < 44 || wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F')
                    throw new InvalidOperationException("无效的WAV文件");

                uint sampleRate = BitConverter.ToUInt32(wavData, 24);
                ushort channels = BitConverter.ToUInt16(wavData, 22);
                ushort bitsPerSample = BitConverter.ToUInt16(wavData, 34);

                if (bitsPerSample != 16)
                    throw new InvalidOperationException($"仅支持16位PCM WAV,当前为{bitsPerSample}位");

                if (sampleRate < 2000 || sampleRate > 256000)
                    throw new InvalidOperationException($"采样率{sampleRate}Hz超出支持范围(2000-256000Hz)");

                if (channels == 0 || channels > 16)
                    throw new InvalidOperationException($"声道数{channels}超出支持范围(1-16)");

                int dataOffset = 12;
                int dataSize = 0;

                while (dataOffset + 8 <= wavData.Length)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wavData, dataOffset, 4);
                    int chunkSize = BitConverter.ToInt32(wavData, dataOffset + 4);

                    if (chunkId == "data")
                    {
                        dataOffset += 8;
                        dataSize = chunkSize;
                        break;
                    }

                    dataOffset += 8 + chunkSize;
                }

                if (dataSize == 0)
                    throw new InvalidOperationException("未找到数据块");

                if (dataOffset + dataSize > wavData.Length)
                    dataSize = wavData.Length - dataOffset;

                uint pcmDataLen = (uint)dataSize;
                IntPtr pcmPtr = Marshal.AllocHGlobal((int)pcmDataLen);

                try
                {
                    Marshal.Copy(wavData, dataOffset, pcmPtr, (int)pcmDataLen);

                    IntPtr outDataPtr;
                    uint outDataLen;

                    byte result = CompressBinkAudio(pcmPtr, pcmDataLen, sampleRate, (byte)channels, 0, 1, 4096, _allocDelegate, _freeDelegate, out outDataPtr, out outDataLen);

                    if (result != 0)
                    {
                        string errorMsg = result switch
                        {
                            1 => "声道数错误",
                            2 => "采样数错误",
                            3 => "采样率错误",
                            4 => "质量参数错误",
                            5 => "内存分配器错误",
                            6 => "输出参数错误",
                            7 => "Seek表错误",
                            8 => "文件过大",
                            _ => $"未知错误代码:{result}"
                        };
                        throw new Exception($"编码失败:{errorMsg}");
                    }

                    byte[] compressedData = new byte[outDataLen];
                    Marshal.Copy(outDataPtr, compressedData, 0, (int)outDataLen);
                    _freeDelegate(outDataPtr);
                    File.WriteAllBytes(binkaFilePath, compressedData);
                }
                finally
                {
                    Marshal.FreeHGlobal(pcmPtr);
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
    }
}
