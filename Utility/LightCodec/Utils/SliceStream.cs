using System;
using System.IO;

namespace LightCodec
{
    public class SliceStream : ProxyStream
    {
        protected long ThisPosition;

        protected long ThisStart;

        protected long ThisLength;

        //public long SliceBoundLow { get { return ThisStart; } }

        //public long SliceLength { get { return ThisLength; } }

        //public long SliceBoundHigh { get { return ThisStart + ThisLength; } }

        public long SliceLength => ThisLength;

        public long SliceLow => ThisStart;

        public long SliceHigh => ThisStart + ThisLength;

        public static SliceStream CreateWithLength(Stream baseStream, long thisStart = 0, long thisLength = -1, bool? canWrite = null)
        {
            return new SliceStream(baseStream, thisStart, thisLength, canWrite);
        }

        public static SliceStream CreateWithBounds(Stream baseStream, long lowerBound, long upperBound, bool? canWrite = null)
        {
            return new SliceStream(baseStream, lowerBound, upperBound - lowerBound, canWrite);
        }

        protected SliceStream(
            Stream baseStream,
            long thisStart = 0,
            long thisLength = -1,
            bool? canWrite = null,
            bool allowSliceOutsideHigh = true)
            : base(baseStream)
        {
            if (!baseStream.CanSeek) throw new NotImplementedException("ParentStream must be seekable");

            ThisPosition = 0;
            ThisStart = thisStart;
            ThisLength = thisLength == -1 ? baseStream.Length - thisStart : thisLength;

            if (SliceHigh < SliceLow || SliceLow < 0 || SliceHigh < 0 || !allowSliceOutsideHigh &&
                (SliceLow > baseStream.Length || SliceHigh > baseStream.Length))
            {
                throw new InvalidOperationException(
                    $"Trying to SliceStream Parent(Length={baseStream.Length}) Slice({thisStart}-{thisLength})");
            }
        }

        public override long Length => ThisLength;

        public override long Position
        {
            get => ThisPosition;
            set
            {
                if (value < 0) value = 0;
                if (value > Length) value = Length;
                ThisPosition = value;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            //Console.WriteLine("Seek(offset: {0}, origin: {1})", offset, origin);
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position = Position + offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
            }
            //Console.WriteLine("   {0}", Position);
            return Position;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (ParentStream)
            {
                var parentStreamPositionToRestore = ParentStream.Position;
                ParentStream.Position = ThisStart + Position;
                if (Position + count > Length)
                {
                    count = (int)(Length - Position);
                }
                try
                {
                    //Console.WriteLine("Read(position: {0}, count: {1})", Position, count);
                    return base.Read(buffer, offset, count);
                }
                finally
                {
                    Seek(count, SeekOrigin.Current);
                    ParentStream.Position = parentStreamPositionToRestore;
                }
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (ParentStream)
            {
                var parentStreamPositionToRestore = ParentStream.Position;
                ParentStream.Position = ThisStart + Position;
                if (Position + count > Length)
                {
                    //count = (int)(Length - Position);
                    throw new IOException(
                        $"Can't write outside the SliceStream. Trying to Write {count} bytes but only {Length - Position} available.");
                }
                try
                {
                    base.Write(buffer, offset, count);
                }
                finally
                {
                    Seek(count, SeekOrigin.Current);
                    ParentStream.Position = parentStreamPositionToRestore;
                }
            }
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
    }
}