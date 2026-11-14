namespace AnyDrop.Services;

/// <summary>
/// S3-02（与 SEC-07 同根）：文件魔数校验。
///
/// MIME 由上传方自报、不可信：一个可执行文件把 <c>Content-Type</c>
/// 写成 <c>image/png</c> 就能骗过只看 MIME 的检查。已知签名类型必须与
/// 文件头字节一致，否则拒绝；无可靠魔数的类型（纯文本、SVG 等文本格式、
/// <c>application/octet-stream</c>）跳过本检查，仍由 MIME 规范化（S0-02）
/// 与附件处置兜底。
///
/// 唯一调用点是 <see cref="ShareService.SendFileAsync"/>（单次上传与
/// 分片完成都走它），校验发生在建预占位行之前：魔数不符的上传不留任何
/// 数据库行与文件。
/// </summary>
public static class FileSignatureValidator
{
    /// <summary>校验需要读取的最大文件头字节数（WebP/AVI/WAVE 的第二段签名在偏移 8 处）。</summary>
    public const int MaxHeaderBytes = 12;

    /// <summary>
    /// 校验文件头与声明 MIME 是否一致。未知 MIME 返回 true（跳过，不拦截）。
    /// <paramref name="header"/> 短于签名长度时按不符处理——合法文件不会比签名还短。
    /// </summary>
    public static bool IsConsistent(ReadOnlySpan<byte> header, string? mimeType)
    {
        var normalized = MimeTypeHelper.Normalize(mimeType);
        return normalized switch
        {
            "image/jpeg" => StartsWith(header, [0xFF, 0xD8, 0xFF]),
            "image/png" => StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            "image/gif" => StartsWithAscii(header, "GIF8"),
            "image/webp" => StartsWithAscii(header, "RIFF") && SegmentEqualsAscii(header, 8, "WEBP"),
            "image/bmp" => StartsWithAscii(header, "BM"),
            "image/tiff" => StartsWith(header, [0x49, 0x49, 0x2A, 0x00])
                || StartsWith(header, [0x4D, 0x4D, 0x00, 0x2A]),
            "image/avif" => SegmentEqualsAscii(header, 4, "ftypavif"),
            "image/heic" or "image/heif" => SegmentEqualsAscii(header, 4, "ftyp")
                && (SegmentEqualsAscii(header, 8, "hei") || SegmentEqualsAscii(header, 8, "mif1") || SegmentEqualsAscii(header, 8, "msf1")),
            "video/mp4" or "video/quicktime" or "video/m4v" => IsIsoBaseMedia(header),
            "video/webm" or "video/x-matroska" => StartsWith(header, [0x1A, 0x45, 0xDF, 0xA3]),
            "video/x-msvideo" => StartsWithAscii(header, "RIFF") && SegmentEqualsAscii(header, 8, "AVI "),
            "audio/mpeg" => StartsWithAscii(header, "ID3")
                || (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0),
            "audio/wav" or "audio/x-wav" => StartsWithAscii(header, "RIFF") && SegmentEqualsAscii(header, 8, "WAVE"),
            "audio/ogg" => StartsWithAscii(header, "OggS"),
            "audio/flac" => StartsWithAscii(header, "fLaC"),
            "application/pdf" => StartsWithAscii(header, "%PDF"),
            "application/zip"
                or "application/x-zip-compressed"
                or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                or "application/vnd.openxmlformats-officedocument.presentationml.presentation"
                or "application/epub+zip" => StartsWith(header, [0x50, 0x4B, 0x03, 0x04])
                    || StartsWith(header, [0x50, 0x4B, 0x05, 0x06])
                    || StartsWith(header, [0x50, 0x4B, 0x07, 0x08]),
            "application/msword"
                or "application/vnd.ms-excel"
                or "application/vnd.ms-powerpoint" => StartsWith(header, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            "application/x-7z-compressed" => StartsWith(header, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]),
            "application/gzip" or "application/x-gzip" => StartsWith(header, [0x1F, 0x8B]),
            "application/x-rar-compressed" => StartsWith(header, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00])
                || StartsWith(header, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00]),
            _ => true
        };
    }

    /// <summary>
    /// 读取文件头并校验，返回可继续读取完整内容的流。
    /// 校验不通过抛 <see cref="ArgumentException"/>（端点层转 400）。
    /// 可 Seek 的流直接倒回起点；不可 Seek 的流包一层前置缓冲，不丢字节。
    /// </summary>
    public static async Task<Stream> EnsureConsistentAsync(Stream stream, string? mimeType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[MaxHeaderBytes];
        var read = 0;
        while (read < header.Length)
        {
            var n = await stream.ReadAsync(header.AsMemory(read, header.Length - read), ct);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        if (!IsConsistent(header.AsSpan(0, read), mimeType))
        {
            // 不带 paramName：本异常的消息会原样展示给用户，
            // ArgumentException(message, paramName) 会在尾部追加
            // " (Parameter 'mimeType')"，对用户是噪音。
            throw new ArgumentException(
                $"文件内容与声明的文件类型（{MimeTypeHelper.Normalize(mimeType)}）不符。");
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        return new PrependStream(header.AsMemory(0, read), stream);
    }

    // ISO Base Media（MP4/MOV/M4V）：文件以一个 box 开头，前 4 字节是 box
    // 长度，4–7 字节是 box 类型。ftyp 是最常见首 box；moov/mdat/free/skip/
    // wide 开头的无 ftyp 文件同样合法。
    private static bool IsIsoBaseMedia(ReadOnlySpan<byte> header)
        => SegmentEqualsAscii(header, 4, "ftyp")
            || SegmentEqualsAscii(header, 4, "moov")
            || SegmentEqualsAscii(header, 4, "mdat")
            || SegmentEqualsAscii(header, 4, "free")
            || SegmentEqualsAscii(header, 4, "skip")
            || SegmentEqualsAscii(header, 4, "wide");

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature)
        => header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);

    private static bool StartsWithAscii(ReadOnlySpan<byte> header, string ascii)
    {
        if (header.Length < ascii.Length)
        {
            return false;
        }

        for (var i = 0; i < ascii.Length; i++)
        {
            if (header[i] != (byte)ascii[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SegmentEqualsAscii(ReadOnlySpan<byte> header, int offset, string ascii)
        => offset >= 0 && header.Length >= offset + ascii.Length && StartsWithAscii(header[offset..], ascii);

    /// <summary>先吐前置缓冲、再读内层流的只读包装（用于不可 Seek 的上传流）。</summary>
    private sealed class PrependStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
    {
        private int _prefixPosition;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = 0;
            if (_prefixPosition < prefix.Length)
            {
                var n = Math.Min(buffer.Length, prefix.Length - _prefixPosition);
                prefix.Span.Slice(_prefixPosition, n).CopyTo(buffer[..n]);
                _prefixPosition += n;
                read += n;
            }

            if (read < buffer.Length)
            {
                read += inner.Read(buffer[read..]);
            }

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = 0;
            if (_prefixPosition < prefix.Length)
            {
                var n = Math.Min(buffer.Length, prefix.Length - _prefixPosition);
                prefix.Span.Slice(_prefixPosition, n).CopyTo(buffer.Span[..n]);
                _prefixPosition += n;
                read += n;
            }

            if (read < buffer.Length)
            {
                read += await inner.ReadAsync(buffer[read..], ct);
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
