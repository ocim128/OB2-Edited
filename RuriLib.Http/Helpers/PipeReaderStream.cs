using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Http.Helpers;

/// <summary>
/// A Stream that wraps a PipeReader for efficient, low-allocation content reading.
/// </summary>
public class PipeReaderStream : Stream
{
    private PipeReader _reader;
    private readonly bool _leaveOpen;

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
        // Argument validation (can be simplified in modern .NET but kept for clarity)
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (buffer.Length - offset < count) throw new ArgumentException("Invalid offset or count for the buffer size.");

        if (count == 0)
        {
            return 0;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Wait for data to be available in the pipe
            ReadResult result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> readableBuffer = result.Buffer;

            // Determine how many bytes we can actually copy
            int bytesToCopy = (int)Math.Min(count, readableBuffer.Length);

            if (bytesToCopy > 0)
            {
                // Get the portion of the buffer to copy
                ReadOnlySequence<byte> slice = readableBuffer.Slice(0, bytesToCopy);

                // Use the efficient built-in CopyTo method
                slice.CopyTo(buffer.AsSpan(offset, bytesToCopy));

                // Tell the pipe reader that we've consumed the data
                _reader.AdvanceTo(slice.End);

                return bytesToCopy;
            }

            // If no bytes were copied, we need to check if the stream has ended.
            // We must advance the reader, marking the buffer as examined to avoid an infinite loop.
            _reader.AdvanceTo(readableBuffer.Start, readableBuffer.End);

            if (result.IsCompleted)
            {
                return 0; // End of stream
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
