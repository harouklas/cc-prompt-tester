using System.IO;
using System.Windows.Media.Imaging;

namespace PromptTester.Wpf.Services;

internal sealed record EncodedImage(string DataUrl, string DisplayName, long EncodedByteCount);

internal static class ImageInputEncoder
{
    private const long MaxDecodedPixelsPerFrame = 40_000_000;

    public static IReadOnlyList<EncodedImage> Encode(
        string imagePath,
        long maxEncodedBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxEncodedBytes <= 0)
        {
            throw new InvalidOperationException("The encoded image payload limit has been reached. Split this document into smaller folders.");
        }

        var extension = Path.GetExtension(imagePath).ToLowerInvariant();

        return extension switch
        {
            ".jpg" or ".jpeg" => [EncodeOriginal(imagePath, "image/jpeg", maxEncodedBytes, cancellationToken)],
            ".png" => [EncodeOriginal(imagePath, "image/png", maxEncodedBytes, cancellationToken)],
            ".webp" => [EncodeOriginal(imagePath, "image/webp", maxEncodedBytes, cancellationToken)],
            ".gif" => EncodeGif(imagePath, maxEncodedBytes, cancellationToken),
            ".bmp" or ".tif" or ".tiff" => EncodeFramesAsPng(imagePath, maxEncodedBytes, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported image format: {imagePath}")
        };
    }

    private static EncodedImage EncodeOriginal(
        string path,
        string mimeType,
        long maxEncodedBytes,
        CancellationToken cancellationToken)
    {
        var bytes = ReadAllBytes(path, cancellationToken);
        EnsureWithinEncodedLimit(bytes.LongLength, maxEncodedBytes, path);
        var base64 = Convert.ToBase64String(bytes);
        return new EncodedImage($"data:{mimeType};base64,{base64}", Path.GetFileName(path), bytes.LongLength);
    }

    private static IReadOnlyList<EncodedImage> EncodeGif(
        string path,
        long maxEncodedBytes,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count != 1)
        {
            throw new InvalidOperationException($"Animated GIF files are not supported. Convert the file to PNG first: {path}");
        }

        EnsureFrameSizeIsSafe(decoder.Frames[0], path);
        return [EncodeOriginal(path, "image/gif", maxEncodedBytes, cancellationToken)];
    }

    private static IReadOnlyList<EncodedImage> EncodeFramesAsPng(
        string path,
        long maxEncodedBytes,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        var encodedFrames = new List<EncodedImage>(decoder.Frames.Count);
        long totalEncodedBytes = 0;

        for (var index = 0; index < decoder.Frames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFrameSizeIsSafe(decoder.Frames[index], path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(decoder.Frames[index]);
            using var output = new MemoryStream();
            encoder.Save(output);
            totalEncodedBytes = checked(totalEncodedBytes + output.Length);
            EnsureWithinEncodedLimit(totalEncodedBytes, maxEncodedBytes, path);
            var bytes = output.ToArray();
            var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            var suffix = decoder.Frames.Count > 1 ? $" - frame {index + 1}" : "";
            encodedFrames.Add(new EncodedImage(dataUrl, $"{Path.GetFileName(path)}{suffix}", bytes.LongLength));
        }

        return encodedFrames;
    }

    private static void EnsureWithinEncodedLimit(long encodedBytes, long maxEncodedBytes, string path)
    {
        if (encodedBytes > maxEncodedBytes)
        {
            throw new InvalidOperationException($"The encoded image payload is too large to process safely. Split the document into smaller folders: {path}");
        }
    }

    private static void EnsureFrameSizeIsSafe(BitmapFrame frame, string path)
    {
        var pixels = checked((long)frame.PixelWidth * frame.PixelHeight);
        if (pixels > MaxDecodedPixelsPerFrame)
        {
            throw new InvalidOperationException($"An image frame exceeds the safe 40-megapixel conversion limit: {path}");
        }
    }

    private static byte[] ReadAllBytes(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        if (stream.Length > int.MaxValue)
        {
            throw new InvalidOperationException($"Image is too large to load: {path}");
        }

        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, Math.Min(1024 * 1024, bytes.Length - offset));
            if (read == 0)
            {
                throw new EndOfStreamException($"Image ended before it could be read completely: {path}");
            }

            offset += read;
        }

        return bytes;
    }
}
