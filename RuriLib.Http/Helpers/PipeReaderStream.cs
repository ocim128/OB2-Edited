using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Http.Helpers
{
    /// <summary>
    /// A Stream that wraps a PipeReader for efficient, low-allocation content reading.
    /// </summary>
    public class PipeReaderStream : Stream
    {
        private PipeReader _reader;
        private bool _leaveOpen;

        public PipeReaderStream(PipeReader reader, bool leaveOpen = false)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _leaveOpen = leaveOpen;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false; // Cannot seek a pipe
        public override bool CanWrite => false; // Cannot write to a pipe

        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // If the buffer is null or invalid, throw an exception
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0 || count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0) return 0;

            while (true)
            {
                ReadResult result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                System.Buffers.ReadOnlySequence<byte> readableBuffer = result.Buffer;

                if (readableBuffer.IsEmpty && result.IsCompleted)
                {
                    return 0; // End of stream
                }

                // Copy data from the pipe's buffer to the provided buffer
                long bytesToCopy = Math.Min(count, readableBuffer.Length);
                int copiedBytes = 0;

                if (bytesToCopy > 0)
                {
                    // Copy to the user's buffer
                    foreach (var segment in readableBuffer)
                    {
                        var spanToCopy = segment.Span;
                        if (copiedBytes + spanToCopy.Length > bytesToCopy)
                        {
                            spanToCopy = spanToCopy.Slice(0, (int)(bytesToCopy - copiedBytes));
                        }
                        spanToCopy.CopyTo(buffer.AsSpan(offset + copiedBytes));
                        copiedBytes += spanToCopy.Length;

                        if (copiedBytes == bytesToCopy) break; // Finished copying the requested amount
                    }
                }
                
                _reader.AdvanceTo(readableBuffer.Start, readableBuffer.GetPosition(copiedBytes));
                
                if (copiedBytes > 0)
                {
                    return copiedBytes;
                }
                
                if (result.IsCompleted)
                {
                    return 0;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _reader != null)
            {
                if (!_leaveOpen)
                {
                    _reader.Complete();
                }
                _reader = null; // Clear reference
            }
            base.Dispose(disposing);
        }
    }
} 