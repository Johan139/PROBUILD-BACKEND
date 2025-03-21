namespace ProbuildBackend.Middleware
{
    public class ProgressReportingStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly Func<long, Task> _progressCallback;
        private long _bytesRead = 0;

        public ProgressReportingStream(Stream innerStream, Func<long, Task> progressCallback)
        {
            _innerStream = innerStream;
            _progressCallback = progressCallback;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
            _bytesRead += bytesRead;
            await _progressCallback(_bytesRead);
            return bytesRead;
        }

        // Implement other required Stream members
        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }
        public override void Flush() => _innerStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
