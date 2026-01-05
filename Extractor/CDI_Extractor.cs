using System.Runtime.InteropServices;
using System.Text;

namespace super_toolbox
{
    public class CDI_Extractor : BaseExtractor
    {
        public new event EventHandler<string>? ExtractionStarted;
        public new event EventHandler<string>? ExtractionProgress;
        public new event EventHandler<string>? ExtractionError;

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

            var cdiFiles = Directory.EnumerateFiles(directoryPath, "*.cdi", SearchOption.AllDirectories)
                .Union(Directory.EnumerateFiles(directoryPath, "*.cdr", SearchOption.AllDirectories))
                .ToList();

            TotalFilesToExtract = cdiFiles.Count;

            foreach (var cdiFilePath in cdiFiles)
            {
                ThrowIfCancellationRequested(cancellationToken);
                ExtractionProgress?.Invoke(this, $"正在处理文件:{Path.GetFileName(cdiFilePath)}");

                try
                {
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(cdiFilePath);
                    string outputDir = Path.Combine(directoryPath, fileNameWithoutExt);
                    Directory.CreateDirectory(outputDir);

                    string[] args = new string[] { cdiFilePath, outputDir };
                    await Task.Run(() => ExecuteCDIrip(args), cancellationToken);

                    var extractedFilesInDir = Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".cdi", StringComparison.OrdinalIgnoreCase) &&
                               !f.EndsWith(".cdr", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var extractedFile in extractedFilesInDir)
                    {
                        extractedFiles.Add(extractedFile);
                        OnFileExtracted(extractedFile);
                        ExtractionProgress?.Invoke(this, $"已提取:{Path.GetFileName(extractedFile)}");
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
                    ExtractionError?.Invoke(this, $"处理文件{cdiFilePath}时出错:{e.Message}");
                    OnExtractionFailed($"处理文件{cdiFilePath}时出错:{e.Message}");
                }
            }

            if (extractedFiles.Count > 0)
            {
                ExtractionProgress?.Invoke(this, $"处理完成，共提取出{extractedFiles.Count}个文件");
            }
            else
            {
                ExtractionProgress?.Invoke(this, "处理完成，未找到CDI文件");
            }

            OnExtractionCompleted();
        }

        private int ExecuteCDIrip(string[] args)
        {
            if (args.Length < 1)
            {
                return 1;
            }

            string inputFile = args[0];
            string outputDir = args.Length > 1 ? args[1] : Path.GetDirectoryName(inputFile) ?? "";

            try
            {
                MainMethod(inputFile, outputDir);
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        private const long ERR_GENERIC = 0L;
        private const long ERR_OPENIMAGE = 0x01;
        private const long ERR_SAVETRACK = 0x02;
        private const long ERR_READIMAGE = 0x03;
        private const long ERR_SAVEIMAGE = 0x04;
        private const long ERR_PATH = 0x05;

        private const uint CDI_V2 = 0x80000004;
        private const uint CDI_V3 = 0x80000005;
        private const uint CDI_V35 = 0x80000006;

        private const int DEFAULT_FORMAT = 0;
        private const int ISO_FORMAT = 1;
        private const int BIN_FORMAT = 2;
        private const int MAC_FORMAT = 3;

        private const int WAV_FORMAT = 0;
        private const int RAW_FORMAT = 1;
        private const int CDA_FORMAT = 2;
        private const int AIFF_FORMAT = 3;

        private const int SHOW_INTERVAL = 2000;
        private const int READ_BUF_SIZE = 1024 * 1024;
        private const int WRITE_BUF_SIZE = 1024 * 1024;

        private struct ImageS
        {
            public uint HeaderOffset;
            public long HeaderPosition;
            public long Length;
            public uint Version;
            public ushort Sessions;
            public ushort Tracks;
            public ushort RemainingSessions;
            public ushort RemainingTracks;
            public ushort GlobalCurrentSession;
        }

        private struct TrackS
        {
            public ushort GlobalCurrentTrack;
            public ushort Number;
            public long Position;
            public uint Mode;
            public uint SectorSize;
            public uint SectorSizeValue;
            public int Length;
            public int PregapLength;
            public int TotalLength;
            public uint StartLba;
            public byte FilenameLength;
        }

        private struct OptsS
        {
            public bool ShowInfo;
            public bool CutFirst;
            public bool CutAll;
            public int Convert;
            public bool FullData;
            public int Audio;
            public bool Swap;
            public bool ShowSpeed;
            public bool Pregap;
        }

        private struct FlagsS
        {
            public bool AskForImage;
            public bool AskForDestPath;
            public int DoCut;
            public bool DoConvert;
            public bool CreateCuesheet;
            public bool SaveAsIso;
        }

        private struct BufferS
        {
            public FileStream File;
            public byte[] Ptr;
            public long Index;
            public long Size;
        }

        private void ErrorExit(long errcode, string message)
        {
            ExtractionError?.Invoke(this, message);
            throw new Exception(message);
        }

        private bool CompareArrays(byte[] a1, byte[] a2)
        {
            if (a1.Length != a2.Length) return false;
            for (int i = 0; i < a1.Length; i++)
                if (a1[i] != a2[i]) return false;
            return true;
        }

        private uint tempValue;

        private uint AskType(FileStream fsource, long headerPosition)
        {
            fsource.Position = headerPosition;
            byte[] buffer = new byte[4];
            fsource.Read(buffer, 0, 4);
            tempValue = BitConverter.ToUInt32(buffer, 0);

            if (tempValue != 0)
                fsource.Position += 8;

            fsource.Position += 24;

            byte filenameLength = (byte)fsource.ReadByte();
            fsource.Position += filenameLength;
            fsource.Position += 19;

            fsource.Read(buffer, 0, 4);
            tempValue = BitConverter.ToUInt32(buffer, 0);
            if (tempValue == 0x80000000)
                fsource.Position += 8;

            fsource.Position += 16;

            fsource.Read(buffer, 0, 4);
            uint trackMode = BitConverter.ToUInt32(buffer, 0);

            fsource.Position = headerPosition;
            return trackMode;
        }

        private void CDIReadTrack(FileStream fsource, ImageS image, ref TrackS track)
        {
            byte[] TRACK_START_MARK = { 0, 0, 0x01, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF };
            byte[] currentStartMark = new byte[10];
            byte[] buffer = new byte[4];

            fsource.Read(buffer, 0, 4);
            tempValue = BitConverter.ToUInt32(buffer, 0);
            if (tempValue != 0)
                fsource.Position += 8;

            fsource.Read(currentStartMark, 0, 10);
            if (!CompareArrays(TRACK_START_MARK, currentStartMark))
                ErrorExit(ERR_GENERIC, "不支持的文件格式:找不到轨道开始标记");

            fsource.Read(currentStartMark, 0, 10);
            if (!CompareArrays(TRACK_START_MARK, currentStartMark))
                ErrorExit(ERR_GENERIC, "不支持的文件格式:找不到轨道开始标记");

            fsource.Position += 4;
            track.FilenameLength = (byte)fsource.ReadByte();
            fsource.Position += track.FilenameLength;
            fsource.Position += 11;
            fsource.Position += 4;
            fsource.Position += 4;

            fsource.Read(buffer, 0, 4);
            tempValue = BitConverter.ToUInt32(buffer, 0);
            if (tempValue == 0x80000000)
                fsource.Position += 8;

            fsource.Position += 2;

            fsource.Read(buffer, 0, 4);
            track.PregapLength = BitConverter.ToInt32(buffer, 0);

            fsource.Read(buffer, 0, 4);
            track.Length = BitConverter.ToInt32(buffer, 0);

            fsource.Position += 6;

            fsource.Read(buffer, 0, 4);
            track.Mode = BitConverter.ToUInt32(buffer, 0);

            fsource.Position += 12;

            fsource.Read(buffer, 0, 4);
            track.StartLba = BitConverter.ToUInt32(buffer, 0);

            fsource.Read(buffer, 0, 4);
            track.TotalLength = BitConverter.ToInt32(buffer, 0);

            fsource.Position += 16;

            fsource.Read(buffer, 0, 4);
            track.SectorSizeValue = BitConverter.ToUInt32(buffer, 0);

            switch (track.SectorSizeValue)
            {
                case 0: track.SectorSize = 2048; break;
                case 1: track.SectorSize = 2336; break;
                case 2: track.SectorSize = 2352; break;
                default: ErrorExit(ERR_GENERIC, "不支持的扇区大小"); break;
            }

            if (track.Mode > 2) ErrorExit(ERR_GENERIC, "不支持的文件格式:轨道模式不支持");

            fsource.Position += 29;
            if (image.Version != CDI_V2)
            {
                fsource.Position += 5;
                fsource.Read(buffer, 0, 4);
                tempValue = BitConverter.ToUInt32(buffer, 0);
                if (tempValue == 0xffffffff)
                    fsource.Position += 78;
            }
        }

        private void CDISkipNextSession(FileStream fsource, ImageS image)
        {
            fsource.Position += 4;
            fsource.Position += 8;
            if (image.Version != CDI_V2) fsource.Position += 1;
        }

        private void CDIGetTracks(FileStream fsource, ref ImageS image)
        {
            byte[] buffer = new byte[2];
            fsource.Read(buffer, 0, 2);
            image.Tracks = BitConverter.ToUInt16(buffer, 0);
        }

        private void CDIInit(FileStream fsource, ref ImageS image, string fsourcename)
        {
            image.Length = fsource.Length;

            if (image.Length < 8) ErrorExit(ERR_GENERIC, "镜像文件太短");

            fsource.Position = image.Length - 8;
            byte[] buffer = new byte[8];
            fsource.Read(buffer, 0, 8);
            image.Version = BitConverter.ToUInt32(buffer, 0);
            image.HeaderOffset = BitConverter.ToUInt32(buffer, 4);

            if (image.HeaderOffset == 0) ErrorExit(ERR_GENERIC, "错误的镜像格式");
        }

        private void CDIGetSessions(FileStream fsource, ref ImageS image)
        {
            if (image.Version == CDI_V35)
                fsource.Position = image.Length - image.HeaderOffset;
            else
                fsource.Position = image.HeaderOffset;

            byte[] buffer = new byte[2];
            fsource.Read(buffer, 0, 2);
            image.Sessions = BitConverter.ToUInt16(buffer, 0);
        }

        private void WriteString(FileStream fs, string s)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(s);
            fs.Write(bytes, 0, bytes.Length);
        }

        private void WriteUInt32(FileStream fs, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            fs.Write(bytes, 0, 4);
        }

        private void WriteUInt16(FileStream fs, ushort value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            fs.Write(bytes, 0, 2);
        }

        private void WriteUInt32BE(FileStream fs, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            fs.Write(bytes, 0, 4);
        }

        private void WriteUInt16BE(FileStream fs, ushort value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            fs.Write(bytes, 0, 2);
        }

        private void WriteWavHeader(FileStream fdest, long trackLength)
        {
            uint wTotalLength;
            uint wDataLength;
            uint wHeaderLength = 16;
            ushort wFormat = 1;
            ushort wChannels = 2;
            uint wSampleRate = 44100;
            uint wBitRate = 176400;
            ushort wBlockAlign = 4;
            ushort wBitsPerSample = 16;

            wDataLength = (uint)(trackLength * 2352);
            wTotalLength = wDataLength + 8 + 16 + 12;

            WriteString(fdest, "RIFF");
            WriteUInt32(fdest, wTotalLength);
            WriteString(fdest, "WAVE");
            WriteString(fdest, "fmt ");
            WriteUInt32(fdest, wHeaderLength);
            WriteUInt16(fdest, wFormat);
            WriteUInt16(fdest, wChannels);
            WriteUInt32(fdest, wSampleRate);
            WriteUInt32(fdest, wBitRate);
            WriteUInt16(fdest, wBlockAlign);
            WriteUInt16(fdest, wBitsPerSample);
            WriteString(fdest, "data");
            WriteUInt32(fdest, wDataLength);
        }

        private void WriteAiffHeader(FileStream fdest, long trackLength)
        {
            uint sourceLength, totalLength;
            uint aCommSize = 18;
            ushort aChannels = 2;
            uint aNumFrames;
            ushort aBitsPerSample = 16;
            uint aSampleRate = 44100;
            uint aSsndSize;
            uint aOffset = 0;
            uint aBlockSize = 0;

            sourceLength = (uint)(trackLength * 2352);
            totalLength = sourceLength + 8 + 18 + 8 + 12;
            aNumFrames = sourceLength / 4;
            aSsndSize = sourceLength + 8;

            WriteString(fdest, "FORM");
            WriteUInt32BE(fdest, totalLength);
            WriteString(fdest, "AIFF");
            WriteString(fdest, "COMM");
            WriteUInt32BE(fdest, aCommSize);
            WriteUInt16BE(fdest, aChannels);
            WriteUInt32BE(fdest, aNumFrames);
            WriteUInt16BE(fdest, aBitsPerSample);
            WriteIeeeExtended(fdest, (double)aSampleRate);
            WriteString(fdest, "SSND");
            WriteUInt32BE(fdest, aSsndSize);
            WriteUInt32BE(fdest, aOffset);
            WriteUInt32BE(fdest, aBlockSize);
        }

        private void WriteIeeeExtended(FileStream fdest, double x)
        {
            byte[] buf = new byte[10];
            ConvertToIeeeExtended(x, buf);
            fdest.Write(buf, 0, 10);
        }

        private uint FloatToUnsigned(double f)
        {
            return (uint)(((long)(f - 2147483648.0)) + 2147483647L + 1);
        }

        private double Frexp(double x, out int exponent)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF) - 1022;
            exponent = exp;
            return x * Math.Pow(2, -exp);
        }

        private double Ldexp(double x, int exponent)
        {
            return x * Math.Pow(2, exponent);
        }

        private void ConvertToIeeeExtended(double num, byte[] bytes)
        {
            int sign;
            int expon;
            double fMant, fsMant;
            uint hiMant, loMant;

            if (num < 0)
            {
                sign = 0x8000;
                num *= -1;
            }
            else
            {
                sign = 0;
            }

            if (num == 0)
            {
                expon = 0;
                hiMant = 0;
                loMant = 0;
            }
            else
            {
                fMant = Frexp(num, out expon);
                if ((expon > 16384) || !(fMant < 1))
                {
                    expon = sign | 0x7FFF;
                    hiMant = 0;
                    loMant = 0;
                }
                else
                {
                    expon += 16382;
                    if (expon < 0)
                    {
                        fMant = Ldexp(fMant, expon);
                        expon = 0;
                    }
                    expon |= sign;
                    fMant = Ldexp(fMant, 32);
                    fsMant = Math.Floor(fMant);
                    hiMant = FloatToUnsigned(fsMant);
                    fMant = Ldexp(fMant - fsMant, 32);
                    fsMant = Math.Floor(fMant);
                    loMant = FloatToUnsigned(fsMant);
                }
            }

            bytes[0] = (byte)(expon >> 8);
            bytes[1] = (byte)expon;
            bytes[2] = (byte)(hiMant >> 24);
            bytes[3] = (byte)(hiMant >> 16);
            bytes[4] = (byte)(hiMant >> 8);
            bytes[5] = (byte)hiMant;
            bytes[6] = (byte)(loMant >> 24);
            bytes[7] = (byte)(loMant >> 16);
            bytes[8] = (byte)(loMant >> 8);
            bytes[9] = (byte)loMant;
        }

        private int BufWrite(byte[] data, long dataSize, ref BufferS buffer)
        {
            long writeLength;

            if (dataSize > (buffer.Size + (buffer.Size - buffer.Index - 1)))
                return 0;

            if (buffer.Index + dataSize < buffer.Size)
            {
                Array.Copy(data, 0, buffer.Ptr, (int)buffer.Index, (int)dataSize);
                buffer.Index += dataSize;
            }
            else
            {
                writeLength = buffer.Size - buffer.Index;
                Array.Copy(data, 0, buffer.Ptr, (int)buffer.Index, (int)writeLength);
                buffer.File.Write(buffer.Ptr, 0, (int)buffer.Size);
                Array.Copy(data, (int)writeLength, buffer.Ptr, 0, (int)(dataSize - writeLength));
                buffer.Index = dataSize - writeLength;
            }

            return 1;
        }

        private int BufWriteFlush(ref BufferS buffer)
        {
            buffer.File.Write(buffer.Ptr, 0, (int)buffer.Index);
            buffer.Index = 0;
            return 1;
        }

        private int BufRead(byte[] data, long dataSize, ref BufferS buffer, long filesize)
        {
            long readLength, maxLength, pos;

            if (dataSize > (buffer.Size + (buffer.Size - buffer.Index - 1)))
                return 0;

            if (filesize == 0)
            {
                maxLength = buffer.Size;
            }
            else
            {
                pos = buffer.File.Position;
                if (pos > filesize) maxLength = 0;
                else maxLength = ((pos + buffer.Size) > filesize) ? (filesize - pos) : buffer.Size;
            }

            if (buffer.Index == 0)
            {
                buffer.File.Read(buffer.Ptr, 0, (int)maxLength);
            }

            if (buffer.Index + dataSize <= buffer.Size)
            {
                Array.Copy(buffer.Ptr, (int)buffer.Index, data, 0, (int)dataSize);
                buffer.Index += dataSize;
                if (buffer.Index >= buffer.Size) buffer.Index = 0;
            }
            else
            {
                readLength = buffer.Size - buffer.Index;
                Array.Copy(buffer.Ptr, (int)buffer.Index, data, 0, (int)readLength);
                buffer.File.Read(buffer.Ptr, 0, (int)maxLength);
                Array.Copy(buffer.Ptr, 0, data, (int)readLength, (int)(dataSize - readLength));
                buffer.Index = dataSize - readLength;
            }

            return 1;
        }

        [DllImport("kernel32.dll")]
        private static extern void QueryPerformanceFrequency(out long lpFrequency);

        [DllImport("kernel32.dll")]
        private static extern void QueryPerformanceCounter(out long lpPerformanceCount);

        private void ShowCounter(int i, long trackLength, long imageLength, long pos)
        {
            int progress = (int)(i * 100 / trackLength);
            int totalProgress = (int)(((pos >> 10) * 100) / (imageLength >> 10));
            Console.Write("\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b" +
                          "[当前进度: {0:00}%  总进度: {1:00}%]  ", progress, totalProgress);
        }

        private void ShowSpeed(uint sectorSize, long frequency, ref long oldCount)
        {
            long performanceCount;
            QueryPerformanceCounter(out performanceCount);
            long lastCount = oldCount;

            long elapsed = (performanceCount - lastCount) / (frequency / 1000);
            long speed = SHOW_INTERVAL * sectorSize / (elapsed > 0 ? elapsed : 1);
            Console.Write("[速度: {0,6} KB/s]  \b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b", speed);

            oldCount = performanceCount;
        }

        private void SaveCuesheet(FileStream fcuesheet, ImageS image, TrackS track, OptsS opts, FlagsS flags)
        {
            string trackFormatString;
            string audioFileExt;

            if (opts.Swap)
                trackFormatString = "MOTOROLA";
            else
                trackFormatString = "BINARY";

            switch (opts.Audio)
            {
                case AIFF_FORMAT:
                    audioFileExt = "aiff";
                    break;
                case CDA_FORMAT:
                    audioFileExt = "cda";
                    break;
                case RAW_FORMAT:
                    audioFileExt = "raw";
                    break;
                case WAV_FORMAT:
                default:
                    audioFileExt = "wav";
                    break;
            }

            byte[] data;
            if (track.Mode == 0)
            {
                if (opts.Audio == WAV_FORMAT)
                {
                    data = Encoding.ASCII.GetBytes(string.Format("FILE taudio{0:00}.wav WAVE\r\n  TRACK {1:00} AUDIO\r\n",
                        track.GlobalCurrentTrack, track.Number));
                }
                else
                {
                    data = Encoding.ASCII.GetBytes(string.Format("FILE taudio{0:00}.{1} {2}\r\n  TRACK {3:00} AUDIO\r\n",
                        track.GlobalCurrentTrack, audioFileExt, trackFormatString, track.Number));
                }

                if (track.GlobalCurrentTrack > 1 && !opts.Pregap && track.PregapLength > 0)
                {
                    byte[] pregapData = Encoding.ASCII.GetBytes("    PREGAP 00:02:00\r\n");
                    byte[] newData = new byte[data.Length + pregapData.Length];
                    Array.Copy(data, newData, data.Length);
                    Array.Copy(pregapData, 0, newData, data.Length, pregapData.Length);
                    data = newData;
                }
            }
            else
            {
                if (flags.SaveAsIso)
                {
                    data = Encoding.ASCII.GetBytes(string.Format("FILE tdata{0:00}.iso BINARY\r\n  TRACK {1:00} MODE{2}/2048\r\n",
                        track.GlobalCurrentTrack, track.Number, track.Mode));
                }
                else
                {
                    data = Encoding.ASCII.GetBytes(string.Format("FILE tdata{0:00}.bin BINARY\r\n  TRACK {1:00} MODE{2}/{3}\r\n",
                        track.GlobalCurrentTrack, track.Number, track.Mode, track.SectorSize));
                }
            }

            byte[] indexData = Encoding.ASCII.GetBytes("    INDEX 01 00:00:00\r\n");
            byte[] finalData = new byte[data.Length + indexData.Length];
            Array.Copy(data, finalData, data.Length);
            Array.Copy(indexData, 0, finalData, data.Length, indexData.Length);

            if (opts.Pregap && track.Mode != 0 && image.RemainingTracks > 1)
            {
                byte[] postgapData = Encoding.ASCII.GetBytes("  POSTGAP 00:02:00\r\n");
                byte[] newFinalData = new byte[finalData.Length + postgapData.Length];
                Array.Copy(finalData, newFinalData, finalData.Length);
                Array.Copy(postgapData, 0, newFinalData, finalData.Length, postgapData.Length);
                finalData = newFinalData;
            }

            fcuesheet.Write(finalData, 0, finalData.Length);
        }

        private void SaveTrack(FileStream fsource, ImageS image, ref TrackS track, OptsS opts, FlagsS flags)
        {
            long trackLength;
            uint headerLength = 0;
            byte tmpVal;
            bool allFine;
            byte[] buffer = new byte[2352];
            string filename;

            long frequency = 0, oldCount = 0;

            byte[] globalReadBufferPtr = new byte[READ_BUF_SIZE];
            byte[] globalWriteBufferPtr = new byte[WRITE_BUF_SIZE];

            if (globalReadBufferPtr == null || globalWriteBufferPtr == null)
            {
                ErrorExit(ERR_GENERIC, "缓冲区内存不足");
                return;
            }

            BufferS readBuffer = new BufferS();
            BufferS writeBuffer = new BufferS();

            if (opts.ShowSpeed)
            {
                QueryPerformanceFrequency(out frequency);
                QueryPerformanceCounter(out oldCount);
            }

            fsource.Position = track.Position;

            if (track.Mode == 0)
            {
                switch (opts.Audio)
                {
                    case RAW_FORMAT:
                        filename = string.Format("taudio{0:00}.raw", track.GlobalCurrentTrack);
                        break;
                    case CDA_FORMAT:
                        filename = string.Format("taudio{0:00}.cda", track.GlobalCurrentTrack);
                        break;
                    case AIFF_FORMAT:
                        filename = string.Format("taudio{0:00}.aiff", track.GlobalCurrentTrack);
                        break;
                    case WAV_FORMAT:
                    default:
                        filename = string.Format("taudio{0:00}.wav", track.GlobalCurrentTrack);
                        break;
                }
            }
            else
            {
                if (flags.SaveAsIso)
                    filename = string.Format("tdata{0:00}.iso", track.GlobalCurrentTrack);
                else
                    filename = string.Format("tdata{0:00}.bin", track.GlobalCurrentTrack);
            }

            FileStream fdest;
            try
            {
                fdest = new FileStream(filename, FileMode.Create, FileAccess.Write);
            }
            catch
            {
                ErrorExit(ERR_SAVETRACK, filename);
                return;
            }

            readBuffer.File = fsource;
            readBuffer.Size = READ_BUF_SIZE;
            readBuffer.Index = 0;
            readBuffer.Ptr = globalReadBufferPtr;
            writeBuffer.File = fdest;
            writeBuffer.Size = WRITE_BUF_SIZE;
            writeBuffer.Index = 0;
            writeBuffer.Ptr = globalWriteBufferPtr;

            fsource.Position += track.PregapLength * track.SectorSize;

            if (flags.DoCut != 0) Console.Write("[剪切: {0}] ", flags.DoCut);

            trackLength = track.Length - flags.DoCut;

            if (opts.Pregap && track.Mode == 0 && image.RemainingTracks > 1)
                trackLength += track.PregapLength;

            if (flags.DoConvert)
                Console.WriteLine("[ISO]");
            else
                Console.WriteLine();

            if (flags.DoConvert)
            {
                if (track.Mode == 2)
                {
                    switch (track.SectorSize)
                    {
                        case 2352: headerLength = 24; break;
                        case 2336: headerLength = 8; break;
                        default: headerLength = 0; break;
                    }
                }
                else
                {
                    switch (track.SectorSize)
                    {
                        case 2352: headerLength = 16; break;
                        case 2048:
                        default: headerLength = 0; break;
                    }
                }
            }

            if (track.Mode == 0)
            {
                switch (opts.Audio)
                {
                    case WAV_FORMAT:
                        WriteWavHeader(fdest, trackLength);
                        break;
                    case AIFF_FORMAT:
                        WriteAiffHeader(fdest, trackLength);
                        break;
                }
            }

            for (int i = 0; i < trackLength; i++)
            {
                if ((i % 128) == 0) ShowCounter(i, trackLength, image.Length, fsource.Position);

                BufRead(buffer, track.SectorSize, ref readBuffer, image.Length);

                if (track.Mode == 0 && opts.Swap)
                {
                    for (int ii = 0; ii < track.SectorSize; ii += 2)
                    {
                        tmpVal = buffer[ii];
                        buffer[ii] = buffer[ii + 1];
                        buffer[ii + 1] = tmpVal;
                    }
                }

                byte[] writeData;
                if (flags.DoConvert)
                {
                    if (opts.Convert == MAC_FORMAT)
                    {
                        byte[] macHeader = { 0, 0, 0x08, 0, 0, 0, 0x08, 0 };
                        allFine = BufWrite(macHeader, 8, ref writeBuffer) == 1;
                    }
                    writeData = new byte[2048];
                    Array.Copy(buffer, headerLength, writeData, 0, 2048);
                    allFine = BufWrite(writeData, 2048, ref writeBuffer) == 1;
                }
                else
                {
                    allFine = BufWrite(buffer, track.SectorSize, ref writeBuffer) == 1;
                }

                if (!allFine) ErrorExit(ERR_SAVETRACK, filename);

                if (opts.ShowSpeed && ((i + 1) % SHOW_INTERVAL) == 0)
                    ShowSpeed(track.SectorSize, frequency, ref oldCount);
            }

            Console.Write("\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b" +
                          "\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b" +
                          "                                                          " +
                          "\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b" +
                          "\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b");

            fsource.Position = track.Position;
            fsource.Position += track.TotalLength * track.SectorSize;

            BufWriteFlush(ref writeBuffer);
            fdest.Flush();
            fdest.Close();
        }

        private int MainMethod(string inputFile, string outputDir)
        {
            string cuesheetname = "", filename = inputFile, destpath = outputDir ?? "";
            FileStream fsource;
            FileStream? fcuesheet = null;

            ImageS image = new ImageS();
            TrackS track = new TrackS();
            OptsS opts = new OptsS();
            FlagsS flags = new FlagsS();

            image.GlobalCurrentSession = 0;
            track.GlobalCurrentTrack = 0;
            track.Position = 0;

            flags.AskForImage = true;
            flags.AskForDestPath = true;

            opts.Audio = WAV_FORMAT;
            opts.Convert = ISO_FORMAT;

            outputDir = outputDir ?? string.Empty;
            string[] args = new string[] { inputFile, outputDir };

            if (args.Length >= 1)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i][0] == '-' || args[i][0] == '/')
                    {
                        string arg = args[i].Substring(1).ToLower();
                        if (arg == "iso") opts.Convert = ISO_FORMAT;
                        if (arg == "bin") opts.Convert = BIN_FORMAT;
                        if (arg == "mac") opts.Convert = MAC_FORMAT;
                        if (arg == "raw") opts.Audio = RAW_FORMAT;
                        if (arg == "cda") opts.Audio = CDA_FORMAT;
                        if (arg == "wav") opts.Audio = WAV_FORMAT;
                        if (arg == "aiff") opts.Audio = AIFF_FORMAT;
                        if (arg == "info") opts.ShowInfo = true;
                        if (arg == "cut") opts.CutFirst = true;
                        if (arg == "cutall") opts.CutAll = true;
                        if (arg == "full") opts.FullData = true;
                        if (arg == "swap") opts.Swap = true;
                        if (arg == "speed") opts.ShowSpeed = true;
                        if (arg == "pregap") opts.Pregap = true;
                        if (arg == "cdrecord") { opts.CutAll = true; opts.Convert = ISO_FORMAT; }
                        if (arg == "winoncd") { opts.CutAll = true; opts.Convert = ISO_FORMAT; opts.Audio = RAW_FORMAT; }
                        if (arg == "fireburner") { opts.CutAll = true; opts.Convert = BIN_FORMAT; }
                    }
                    else if (i == 0) flags.AskForImage = false;
                    else if (i == 1) flags.AskForDestPath = false;
                }
            }

            if (flags.AskForImage)
            {
                Console.WriteLine("错误:语法不正确\n\n用法: cdirip image.cdi [目标路径] [选项]");
                return 1;
            }
            else
            {
                filename = args[0];
            }

            Console.WriteLine("搜索文件: '{0}'", filename);

            try
            {
                fsource = new FileStream(filename, FileMode.Open, FileAccess.Read);
            }
            catch
            {
                filename += ".cdi";
                Console.WriteLine("未找到。尝试'{0}'...", filename);
                try
                {
                    fsource = new FileStream(filename, FileMode.Open, FileAccess.Read);
                }
                catch
                {
                    ErrorExit(ERR_OPENIMAGE, filename);
                    return 1;
                }
            }

            Console.WriteLine("找到镜像文件。正在打开...");

            CDIInit(fsource, ref image, filename);

            if (image.Version == CDI_V2)
                Console.WriteLine("这是 v2.0 镜像");
            else if (image.Version == CDI_V3)
                Console.WriteLine("这是 v3.0 镜像");
            else if (image.Version == CDI_V35)
                Console.WriteLine("这是 v3.5 镜像");
            else
                ErrorExit(ERR_GENERIC, "不支持的镜像版本");

            if (!opts.ShowInfo)
            {
                if (!flags.AskForDestPath)
                {
                    destpath = args[1];
                    Console.WriteLine("目标路径: '{0}'", destpath);
                    try
                    {
                        Directory.SetCurrentDirectory(destpath);
                    }
                    catch
                    {
                        ErrorExit(ERR_PATH, destpath);
                    }
                }
            }

            switch (opts.Audio)
            {
                case AIFF_FORMAT:
                case CDA_FORMAT:
                    opts.Swap = !opts.Swap;
                    break;
            }

            Console.WriteLine("\n正在分析镜像...");

            CDIGetSessions(fsource, ref image);

            if (image.Sessions == 0) ErrorExit(ERR_GENERIC, "错误格式:找不到文件头");

            Console.WriteLine("找到 {0} 个会话", image.Sessions);

            if (!opts.ShowInfo) Console.WriteLine("\n正在提取镜像... (随时按Ctrl+C退出)");

            if (opts.Pregap) Console.WriteLine("将保存间隙数据");

            image.RemainingSessions = image.Sessions;

            while (image.RemainingSessions > 0)
            {
                image.GlobalCurrentSession++;

                CDIGetTracks(fsource, ref image);

                image.HeaderPosition = fsource.Position;

                Console.WriteLine("\n会话 {0} 有 {1} 个轨道", image.GlobalCurrentSession, image.Tracks);

                if (image.Tracks == 0)
                    Console.WriteLine("开放会话");
                else
                {
                    if (!opts.ShowInfo)
                    {
                        if (image.GlobalCurrentSession == 1)
                        {
                            if (AskType(fsource, image.HeaderPosition) == 2)
                            {
                                if (opts.Convert == ISO_FORMAT || opts.Convert == MAC_FORMAT)
                                    flags.CreateCuesheet = false;
                                else
                                {
                                    flags.CreateCuesheet = true;
                                    opts.Convert = BIN_FORMAT;
                                }
                            }
                            else
                                flags.CreateCuesheet = true;
                        }
                        else
                        {
                            if (opts.Convert == BIN_FORMAT)
                                flags.CreateCuesheet = true;
                            else
                                flags.CreateCuesheet = false;
                        }
                    }
                    else
                        flags.CreateCuesheet = false;

                    if (flags.CreateCuesheet)
                    {
                        Console.WriteLine("正在创建CUE表...");
                        if (image.GlobalCurrentSession == 1)
                            cuesheetname = "tdisc.cue";
                        else
                            cuesheetname = string.Format("tdisc{0}.cue", image.GlobalCurrentSession);
                        fcuesheet = new FileStream(cuesheetname, FileMode.Create, FileAccess.Write);
                    }

                    image.RemainingTracks = image.Tracks;

                    while (image.RemainingTracks > 0)
                    {
                        track.GlobalCurrentTrack++;
                        track.Number = (ushort)(image.Tracks - image.RemainingTracks + 1);

                        CDIReadTrack(fsource, image, ref track);

                        image.HeaderPosition = fsource.Position;

                        if (!opts.ShowInfo) Console.Write("保存  ");
                        Console.Write("轨道: {0,2}  ", track.GlobalCurrentTrack);
                        Console.Write("类型: ");
                        switch (track.Mode)
                        {
                            case 0: Console.Write("音频/"); break;
                            case 1: Console.Write("模式1/"); break;
                            case 2:
                            default: Console.Write("模式2/"); break;
                        }
                        Console.Write("{0}  ", track.SectorSize);
                        if (opts.Pregap)
                            Console.Write("间隙: {0,-3}  ", track.PregapLength);
                        Console.Write("大小: {0,-6}  ", track.Length);
                        Console.Write("LBA: {0,-6}  ", track.StartLba);

                        if (opts.ShowInfo) Console.WriteLine();

                        if (track.Length < 0 && opts.Pregap == false)
                            ErrorExit(ERR_GENERIC, "发现负的轨道大小\n你必须使用 /pregap 选项提取镜像");

                        if (!opts.FullData && track.Mode != 0 && image.GlobalCurrentSession == 1 && image.Sessions > 1)
                            flags.DoCut = 2;
                        else if (!(track.Mode != 0 && opts.FullData))
                            flags.DoCut = ((opts.CutAll) ? 2 : 0) + ((opts.CutFirst && track.GlobalCurrentTrack == 1) ? 2 : 0);
                        else flags.DoCut = 0;

                        if (track.Mode != 0 && track.SectorSize != 2048)
                        {
                            switch (opts.Convert)
                            {
                                case BIN_FORMAT: flags.DoConvert = false; break;
                                case ISO_FORMAT: flags.DoConvert = true; break;
                                case MAC_FORMAT: flags.DoConvert = true; break;
                                case DEFAULT_FORMAT:
                                default:
                                    if (track.Mode == 1)
                                        flags.DoConvert = true;
                                    else
                                        if (image.GlobalCurrentSession > 1)
                                        flags.DoConvert = true;
                                    else
                                        flags.DoConvert = false;
                                    break;
                            }
                        }
                        else
                            flags.DoConvert = false;

                        if (track.SectorSize == 2048 || (track.Mode != 0 && flags.DoConvert))
                            flags.SaveAsIso = true;
                        else
                            flags.SaveAsIso = false;

                        if (!opts.ShowInfo)
                        {
                            if (track.TotalLength < track.Length + track.PregapLength)
                            {
                                Console.WriteLine("\n此轨道似乎被截断。正在跳过...");
                                fsource.Position = track.Position;
                                fsource.Position += track.TotalLength;
                                track.Position = fsource.Position;
                            }
                            else
                            {
                                SaveTrack(fsource, image, ref track, opts, flags);
                                track.Position = fsource.Position;

                                if (flags.CreateCuesheet && !(track.Mode == 2 && flags.DoConvert) && fcuesheet != null)
                                    SaveCuesheet(fcuesheet, image, track, opts, flags);
                            }
                        }

                        fsource.Position = image.HeaderPosition;
                        image.RemainingTracks--;
                    }

                    if (flags.CreateCuesheet && fcuesheet != null) fcuesheet.Close();
                }

                CDISkipNextSession(fsource, image);
                image.RemainingSessions--;
            }

            Console.WriteLine("\n全部完成!");
            if (!opts.ShowInfo) Console.WriteLine("祝刻录顺利...");
            fsource.Close();

            return 0;
        }
    }
}