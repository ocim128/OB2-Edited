using System;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="PipeReaderStream"/> class.
    /// </summary>
    /// <param name="reader">The PipeReader to wrap.</param>
    /// <param name="leaveOpen">Whether to leave the PipeReader open when disposing.</param>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public PipeReaderStream(PipeReader reader, bool leaveOpen = false)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Gets a value indicating whether the current stream supports reading.
    /// </summary>
    public override bool CanRead => true;
    
    /// <summary>
    /// Gets a value indicating whether the current stream supports seeking.
    /// </summary>
    public override bool CanSeek => false; // Cannot seek a pipe
    
    /// <summary>
    /// Gets a value indicating whether the current stream supports writing.
    /// </summary>
    public override bool CanWrite => false; // Cannot write to a pipe

    /// <summary>
    /// Gets the length in bytes of the stream.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown as pipes do not support length.</exception>
    public override long Length => throw new NotSupportedException();
    
    /// <summary>
    /// Gets or sets the position within the current stream.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown as pipes do not support positioning.</exception>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown as pipes do not support flushing.</exception>
    public override void Flush() => throw new NotSupportedException();
    
    /// <summary>
    /// Sets the position within the current stream.
    /// </summary>
    /// <param name="offset">A byte offset relative to the origin parameter.</param>
    /// <param name="origin">A value of type SeekOrigin indicating the reference point used to obtain the new position.</param>
    /// <returns>The new position within the current stream.</returns>
    /// <exception cref="NotSupportedException">Always thrown as pipes do not support seeking.</exception>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    
    /// <summary>
    /// Sets the length of the current stream.
    /// </summary>
    /// <param name="value">The desired length of the current stream in bytes.</param>
    /// <exception cref="NotSupportedException">Always thrown as pipes do not support setting length.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();
    
    /// <summary>
    /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
    /// </summary>
    /// <param name="buffer">An array of bytes.</param>
    /// <param name="offset">The zero-based byte offset in buffer at which to begin copying bytes to the current stream.</param>
    /// <param name="count">The number of bytes to be written to the current stream.</param>
    /// <exception cref="NotSupportedException">Always thrown as pipes do not support writing.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer">An array of bytes.</param>
    /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
    /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
    /// <returns>The total number of bytes read into the buffer.</returns>
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    /// <summary>
    /// Asynchronously reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer">The buffer to write the data into.</param>
    /// <param name="offset">The byte offset in buffer at which to begin writing data from the stream.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous read operation. The value contains the total number of bytes read into the buffer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when buffer is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when offset or count is out of range.</exception>
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

        if (count == 0)
        {
            return 0;
        }

        while (true)
        {
            ReadResult result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var readableBuffer = result.Buffer;

            if (readableBuffer.IsEmpty && result.IsCompleted)
            {
                return 0; // End of stream
            }

            // Copy data from the pipe's buffer to the provided buffer
            var bytesToCopy = Math.Min(count, readableBuffer.Length);
            int copiedBytes = 0;

            if (bytesToCopy > 0)
            {
                // Copy to the user's buffer
                foreach (var segment in readableBuffer)
                {
                    // Convert span to array immediately to avoid async span usage
                    var segmentArray = segment.Span.ToArray();
                    var arrayToCopy = segmentArray;
                    
                    if (copiedBytes + arrayToCopy.Length > bytesToCopy)
                    {
                        arrayToCopy = new byte[(int)(bytesToCopy - copiedBytes)];
                        Array.Copy(segmentArray, 0, arrayToCopy, 0, arrayToCopy.Length);
                    }
                    
                    arrayToCopy.CopyTo(buffer, offset + copiedBytes);
                    copiedBytes += arrayToCopy.Length;

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

    /// <summary>
    /// Releases the unmanaged resources used by the Stream and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
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
