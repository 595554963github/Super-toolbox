using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

#pragma warning disable CS8600
#pragma warning disable CS8605

namespace LightCodec
{
    public static class StreamExtensions
    {
        public static SliceStream Slice(this Stream baseStream)
        {
            return SliceStream.CreateWithLength(baseStream);
        }

        public static byte[] ToArray(this Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (stream is MemoryStream memoryStream)
            {
                return memoryStream.ToArray();
            }

            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public static MemoryStream ReadStreamCopy(this Stream stream, long toRead = -1)
        {
            if (toRead == -1) toRead = stream.Available();
            return new MemoryStream(stream.ReadBytes((int)toRead));
        }

        public static bool Eof(this Stream stream)
        {
            return stream.Available() <= 0;
        }

        public static TStream PreservePositionAndLock<TStream>(this TStream stream, long position, Action callback) where TStream : Stream
        {
            return stream.PreservePositionAndLock(() =>
            {
                stream.Position = position;
                callback();
            });
        }

        public static TStream PreservePositionAndLock<TStream>(this TStream stream, Action callback) where TStream : Stream
        {
            return stream.PreservePositionAndLock(str => { callback(); });
        }

        public static TStream PreservePositionAndLock<TStream>(this TStream stream, Action<Stream> callback) where TStream : Stream
        {
            if (!stream.CanSeek)
            {
                throw new NotImplementedException("Stream can't seek");
            }

            lock (stream)
            {
                var oldPosition = stream.Position;
                {
                    callback(stream);
                }
                stream.Position = oldPosition;
            }
            return stream;
        }

        public static SliceStream SliceWithLength(this Stream baseStream, long thisStart = 0, long thisLength = -1, bool? canWrite = null)
        {
            return SliceStream.CreateWithLength(baseStream, thisStart, thisLength, canWrite);
        }

        public static SliceStream ReadStream(this Stream stream, long toRead = -1)
        {
            if (toRead == -1) toRead = stream.Available();
            var readedStream = SliceStream.CreateWithLength(stream, stream.Position, toRead);
            stream.Skip(toRead);
            return readedStream;
        }

        public static long Available(this Stream stream)
        {
            return stream.Length - stream.Position;
        }

        public static byte[] ReadChunk(this Stream stream, int start, int length)
        {
            var chunk = new byte[length];
            stream.PreservePositionAndLock(() =>
            {
                stream.Position = start;
                stream.Read(chunk, 0, length);
            });
            return chunk;
        }

        public static byte[] ReadUntil(this Stream stream, byte expectedByte, bool includeExpectedByte = false)
        {
            var found = false;
            var buffer = new MemoryStream();
            while (!found)
            {
                var b = stream.ReadByte();
                if (b == -1) throw new Exception("End Of Stream");

                if (b == expectedByte)
                {
                    found = true;
                    if (!includeExpectedByte) break;
                }

                buffer.WriteByte((byte)b);
            }
            return buffer.ToArray();
        }

        public static string ReadString(this Stream stream, int length, int offset = 0)
        {
            var buffer = new byte[length];
            stream.Seek(offset, SeekOrigin.Current);
            stream.Read(buffer, 0, length);
            return Encoding.ASCII.GetString(buffer);
        }

        public static string ReadUntilString(this Stream stream, byte expectedByte, Encoding encoding, bool includeExpectedByte = false)
        {
            return encoding.GetString(stream.ReadUntil(expectedByte, includeExpectedByte));
        }

        public static string ReadAllContentsAsString(this Stream stream, Encoding? encoding = null, bool fromStart = true)
        {
            encoding ??= Encoding.UTF8;
            var data = stream.ReadAll(fromStart);
            if (Equals(encoding, Encoding.UTF8))
            {
                if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                {
                    data = data.Skip(3).ToArray();
                }
            }
            return encoding.GetString(data);
        }

        public static byte[] ReadAll(this Stream stream, bool fromStart = true, bool dispose = false)
        {
            try
            {
                var memoryStream = new MemoryStream();

                if (fromStart && stream.CanSeek)
                {
                    //if (!Stream.CanSeek) throw (new NotImplementedException("Can't use 'FromStream' on Stream that can't seek"));
                    stream.PreservePositionAndLock(() =>
                    {
                        stream.Position = 0;
                        stream.CopyTo(memoryStream);
                    });
                }
                else
                {
                    stream.CopyTo(memoryStream);
                }

                return memoryStream.ToArray();
            }
            finally
            {
                if (dispose) stream.Dispose();
            }
        }

        public static byte[] ReadBytes(this Stream stream, int toRead)
        {
            if (toRead == 0) return new byte[0];
            var buffer = new byte[toRead];
            var readed = 0;
            while (readed < toRead)
            {
                var readedNow = stream.Read(buffer, readed, toRead - readed);
                if (readedNow <= 0)
                    throw new Exception("Unable to read " + toRead + " bytes, readed " + readed + ".");
                readed += readedNow;
            }
            return buffer;
        }

        public static unsafe T BytesToStruct<T>(byte[] rawData) where T : struct
        {
            T result;

            var expectedLength = Marshal.SizeOf(typeof(T));

            if (rawData.Length < expectedLength)
                throw new Exception($"BytesToStruct. Not enough bytes. Expected: {expectedLength} Provided: {rawData.Length}");

            fixed (byte* rawDataPointer = &rawData[0])
            {
                result = (T)Marshal.PtrToStructure(new IntPtr(rawDataPointer), typeof(T));
            }

            return result;
        }

        public static unsafe byte[] StructToBytes<T>(T data) where T : struct
        {
            var rawData = new byte[Marshal.SizeOf(data)];

            fixed (byte* rawDataPointer = rawData)
            {
                Marshal.StructureToPtr(data, new IntPtr(rawDataPointer), false);
            }

            return rawData;
        }

        public static unsafe byte[] StructArrayToBytes<T>(T[] dataArray, int count = -1) where T : struct
        {
            if (count == -1) count = dataArray.Length;
            var elementSize = Marshal.SizeOf(dataArray[0]);
            var rawData = new byte[elementSize * count];

            fixed (byte* rawDataPointer = rawData)
            {
                for (var n = 0; n < count; n++)
                {
                    Marshal.StructureToPtr(dataArray[n], new IntPtr(rawDataPointer + elementSize * n), false);
                }
            }

            return rawData;
        }

        public static TStream WriteBytes<TStream>(this TStream stream, byte[] bytes) where TStream : Stream
        {
            stream.Write(bytes, 0, bytes.Length);
            return stream;
        }

        public static Stream WriteStruct<T>(this Stream stream, T Struct) where T : struct
        {
            var bytes = StructToBytes(Struct);
            stream.Write(bytes, 0, bytes.Length);
            return stream;
        }

        public static TStream WriteStructVector<T, TStream>(this TStream stream, T[] structs, int count = -1)
            where T : struct
            where TStream : Stream
        {
            stream.WriteBytes(StructArrayToBytes(structs, count));
            return stream;
        }

        public static T ReadStructPartially<T>(this Stream stream) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            var bufferPartial = stream.ReadBytes(Math.Min((int)stream.Available(), size));
            byte[] buffer;
            if (bufferPartial.Length < size)
            {
                buffer = new byte[size];
                bufferPartial.CopyTo(buffer, 0);
            }
            else
            {
                buffer = bufferPartial;
            }
            return BytesToStruct<T>(buffer);
        }

        public static T ReadStruct<T>(this Stream stream) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            var buffer = stream.ReadBytes(size);
            return BytesToStruct<T>(buffer);
        }

        public static TType[] ReadStructVectorAt<TType>(this Stream stream, long offset, uint count, int entrySize = -1) where TType : struct
        {
            TType[] value = null;

            stream.PreservePositionAndLock(() =>
            {
                stream.Position = offset;
                value = stream.ReadStructVector<TType>(count, entrySize);
            });

#pragma warning disable CS8603
            return value;
#pragma warning restore CS8603
        }

        public static TType[] ReadStructVector<TType>(this Stream stream, out TType[] vector, uint count, int entrySize = -1) where TType : struct
        {
            vector = stream.ReadStructVector<TType>(count, entrySize);
            return vector;
        }

        public static unsafe T[] BytesToStructArray<T>(byte[] rawData) where T : struct
        {
            var elementSize = Marshal.SizeOf(typeof(T));
            var array = new T[rawData.Length / elementSize];

            var type = typeof(T);
            fixed (byte* rawDataPointer = &rawData[0])
            {
                for (var n = 0; n < array.Length; n++)
                {
                    array[n] = (T)Marshal.PtrToStructure(new IntPtr(rawDataPointer + n * elementSize), type);
                }
            }

            return array;
        }

        public static TType[] ReadStructVector<TType>(this Stream stream, uint count, int entrySize = -1, bool allowReadLess = false) where TType : struct
        {
            if (count == 0) return new TType[0];

            var itemSize = Marshal.SizeOf(typeof(TType));
            var skipSize = entrySize == -1 ? 0 : entrySize - itemSize;

            var maxCount = (uint)(stream.Length / (itemSize + skipSize));
            if (allowReadLess)
            {
                count = Math.Min(maxCount, count);
            }

            if (skipSize < 0)
                throw new Exception("Invalid Size");
            if (skipSize == 0)
                return BytesToStructArray<TType>(stream.ReadBytes((int)(itemSize * count)));
            var vector = new TType[count];

            for (var n = 0; n < count; n++)
            {
                vector[n] = ReadStruct<TType>(stream);
                stream.Skip(skipSize);
            }

            return vector;
        }

        public static T[] ReadStructVectorUntilTheEndOfStream<T>(this Stream stream) where T : struct
        {
            var entrySize = Marshal.SizeOf(typeof(T));
            var bytesAvailable = stream.Available();
            //Console.WriteLine("BytesAvailable={0}/EntrySize={1}", BytesAvailable, EntrySize);
            return stream.ReadStructVector<T>((uint)(bytesAvailable / entrySize));
        }

        public static long Align(long value, long alignValue)
        {
            if (value % alignValue != 0)
            {
                value += alignValue - value % alignValue;
            }
            return value;
        }

        public static Stream Align(this Stream stream, int align)
        {
            stream.Position = Align(stream.Position, align);
            return stream;
        }

        public static Stream Skip(this Stream stream, long count)
        {
            if (count != 0)
            {
                if (!stream.CanSeek)
                {
                    if (count < 0) throw new NotImplementedException("Can't go back");
                    stream.ReadBytes((int)count);
                }
                else
                {
                    stream.Seek(count, SeekOrigin.Current);
                }
            }
            return stream;
        }

        public static void CopyToFast(this Stream fromStream, Stream toStream, Action<long, long>? actionReport = null)
        {
            var bufferSize = (int)Math.Min(fromStream.Length, 2 * 1024 * 1024);
            var buffer = new byte[bufferSize];
            CopyToFast(fromStream, toStream, buffer, actionReport);
            //buffer = null;
        }

        public static void CopyToFast(this Stream fromStream, Stream toStream, byte[] buffer, Action<long, long>? actionReport = null)
        {
            /// ::TODO: Create a buffer and reuse it once for each thread.
            actionReport ??= (current, max) => { };
            var totalBytes = fromStream.Available();
            var currentBytes = 0;
            actionReport(currentBytes, totalBytes);
            while (true)
            {
                var readed = fromStream.Read(buffer, 0, buffer.Length);
                if (readed <= 0) break;
                toStream.Write(buffer, 0, readed);
                currentBytes += readed;
                actionReport(currentBytes, totalBytes);
            }
            //buffer = null;
        }

        public static Stream CopyToFile(this Stream stream, string fileName)
        {
            try
            {
                var directoryName = Path.GetDirectoryName(fileName);
                if (directoryName != null) Directory.CreateDirectory(directoryName);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            using (var outputFile = File.Open(fileName, FileMode.Create, FileAccess.Write))
            {
                stream.CopyToFast(outputFile);
            }
            return stream;
        }

        public static Stream WriteStream(this Stream toStream, Stream fromStream, Action<long, long>? actionReport = null)
        {
            fromStream.CopyToFast(toStream, actionReport);
            return toStream;
        }

        public static Stream SetPosition(this Stream stream, long position)
        {
            stream.Position = position;
            return stream;
        }

        public static unsafe int ReadToPointer(this Stream stream, byte* pointer, int count)
        {
            var data = new byte[count];
            var result = stream.Read(data, 0, count);
            Marshal.Copy(data, 0, new IntPtr(pointer), count);
            return result;
        }
    }
}

#pragma warning restore CS8605
#pragma warning restore CS8600