
using System.Diagnostics;
using System.Text;

namespace Basic.CompilerLog.Util;

internal sealed class StringStream(string content, Encoding encoding) : Stream
{
    private readonly string _content = content;
    private readonly Encoding _encoding = encoding;
    private int _contentPosition;
    private int _bytePosition;

    // These fields come into play when we have to read portions of a character at a time
    private int _splitPosition = -1;
    private int _splitCount;
    private byte[] _splitBuffer = Array.Empty<byte>();

    internal bool InSplit => _splitPosition >= 0;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position 
    {
        get => _bytePosition;
        set
        {
            if (value != 0)
            {
                throw new NotSupportedException();
            }

            _bytePosition = 0;
            _contentPosition = 0;
            _splitPosition = -1;
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadCore(buffer.AsSpan(offset, count));

#if NET
    public override int Read(Span<byte> buffer) =>
        ReadCore(buffer);
#endif

    private int ReadCore(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        if (_contentPosition >= _content.Length)
        {
            return 0;
        }

        return InSplit
            ? ReadFromSplitChar(buffer)
            : ReadFromString(buffer);
    }

    private int ReadFromSplitChar(Span<byte> buffer)
    {
        Debug.Assert(InSplit);
        Debug.Assert(buffer.Length > 0);
        Debug.Assert(_splitPosition >= 0);
        Debug.Assert(_splitCount > 0 && _splitCount <= _splitBuffer.Length);

        int count = Math.Min(_splitCount - _splitPosition, buffer.Length);
        _splitBuffer.AsSpan(_splitPosition, count).CopyTo(buffer);
        _splitPosition += count;
        if (_splitPosition == _splitBuffer.Length)
        {
            _splitPosition = -1;
            _contentPosition++;
        }

        return count;
    }

    private int ReadFromString(Span<byte> buffer)
    {
        Debug.Assert(!InSplit);
        Debug.Assert(buffer.Length > 0);
        Debug.Assert(_contentPosition < _content.Length);

        var charCount = Math.Min(_content.Length - _contentPosition, 512);
        do
        {
            var charSpan = _content.AsSpan(_contentPosition, charCount);
            var byteCount = _encoding.GetByteCount(charSpan);
            if (byteCount > buffer.Length)
            {
                if (charCount == 1)
                {
                    // Buffer isn't big enough to hold a single character. Need to move into a split character 
                    // mode to handle this case.
                    if (byteCount > _splitBuffer.Length)
                    {
                        _splitBuffer = new byte[byteCount];
                    }

                    _splitPosition = 0;
                    _splitCount = _encoding.GetBytes(_content.AsSpan(_contentPosition, 1), _splitBuffer);
                    Debug.Assert(_splitCount <= _splitBuffer.Length);

                    return ReadFromSplitChar(buffer);
                }

                charCount /= 2;
                continue;
            }

            var read = _encoding.GetBytes(charSpan, buffer);
            Debug.Assert(read == byteCount);
            _contentPosition += charCount;
            return read;
        } while (true);
    }

    public override long Seek(long offset, SeekOrigin origin) => 
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}