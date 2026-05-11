using System.IO;

namespace LightCodec
{
    public class ProxyStream : Stream
    {
        protected Stream ParentStream;
        protected bool CloseParent;

        public ProxyStream(Stream baseStream, bool closeParent = false)
        {
            ParentStream = baseStream;
            CloseParent = closeParent;
        }

        public override bool CanRead => ParentStream.CanRead;

        public override bool CanSeek => ParentStream.CanSeek;

        public override bool CanWrite => ParentStream.CanWrite;

        public override void Flush()
        {
            ParentStream.Flush();
        }

        public override long Length => ParentStream.Length;

        public override long Position
        {
            get => ParentStream.Position;
            set => ParentStream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ParentStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return ParentStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            ParentStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ParentStream.Write(buffer, offset, count);
        }

        public override void Close()
        {
            if (CloseParent) ParentStream.Close();
            base.Close();
        }

        /*
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
        */
    }
}