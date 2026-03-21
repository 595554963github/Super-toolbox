using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace super_toolbox
{
    public class Wav2rada_Converter : BaseExtractor
    {
        private static string? _tempDllPath;
        private static bool _dllLoaded;

        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;

        static Wav2rada_Converter()
        {
            try
            {
                _tempDllPath = LoadEmbeddedDll("embedded.RadAudioCodec.dll", "RadAudioCodec_Encode.dll");
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

        [DllImport("RadAudioCodec_Encode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint RadAudioGetBuildVersion();

        [DllImport("RadAudioCodec_Encode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr RadAudioGetErrorString(byte errorCode);

        [DllImport("RadAudioCodec_Encode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte RadAudioEncodeFile(
            IntPtr pcmData,
            ulong pcmDataLen,
            uint sampleRate,
            byte channels,
            byte quality,
            byte seamlessLooping,
            byte generateSeekTable,
            ushort seekTableMaxEntries,
            out IntPtr outData,
            out ulong outDataLen
        );

        [DllImport("RadAudioCodec_Encode.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void RadAudioFreeEncoderData(IntPtr data);

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
                    string radaFilePath = Path.Combine(fileDirectory, $"{fileName}.rada");

                    try
                    {
                        if (File.Exists(radaFilePath)) File.Delete(radaFilePath);

                        bool conversionSuccess = await Task.Run(() => ConvertWavToRada(wavFilePath, radaFilePath, cancellationToken));

                        if (conversionSuccess && File.Exists(radaFilePath))
                        {
                            successCount++;
                            ConversionProgress?.Invoke(this, $"转换成功:{Path.GetFileName(radaFilePath)}");
                            OnFileConverted(radaFilePath);
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

        private bool ConvertWavToRada(string wavFilePath, string radaFilePath, CancellationToken cancellationToken)
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

                if (sampleRate != 48000 && sampleRate != 44100 && sampleRate != 32000 && sampleRate != 24000)
                    throw new InvalidOperationException($"采样率{sampleRate}Hz不支持,仅支持24000,32000,44100,48000Hz");

                if (channels < 1 || channels > 32)
                    throw new InvalidOperationException($"声道数{channels}超出支持范围(1-32)");

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

                ulong pcmDataLen = (ulong)dataSize;
                IntPtr pcmPtr = Marshal.AllocHGlobal((int)pcmDataLen);

                try
                {
                    Marshal.Copy(wavData, dataOffset, pcmPtr, (int)pcmDataLen);

                    IntPtr outDataPtr;
                    ulong outDataLen;

                    byte result = RadAudioEncodeFile(pcmPtr, pcmDataLen, sampleRate, (byte)channels, 5, 0, 1, 4096, out outDataPtr, out outDataLen);

                    if (result != 0)
                    {
                        IntPtr errorMsgPtr = RadAudioGetErrorString(result);
                        string errorMsg = Marshal.PtrToStringAnsi(errorMsgPtr) ?? $"未知错误代码:{result}";
                        throw new Exception($"编码失败:{errorMsg}");
                    }

                    byte[] compressedData = new byte[outDataLen];
                    Marshal.Copy(outDataPtr, compressedData, 0, (int)outDataLen);
                    RadAudioFreeEncoderData(outDataPtr);
                    File.WriteAllBytes(radaFilePath, compressedData);
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