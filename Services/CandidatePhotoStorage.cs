using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace voting.Services;

public sealed record PhotoSaveResult(bool Success, string? FileName, string? ContentType, string? ErrorMessage);

public partial class CandidatePhotoStorage
{
    public const int MaxFileBytes = 2 * 1024 * 1024;
    private const int MinFileBytes = 32;
    private const int MaxDimension = 4096;

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PngIend = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    private readonly string _baseDirectory;

    public CandidatePhotoStorage(IHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration["CandidatePhotos:StoragePath"];
        var rawPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "candidate-photos")
            : (Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
        _baseDirectory = Path.GetFullPath(rawPath);
    }

    public async Task<PhotoSaveResult> ValidateAndSaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return new PhotoSaveResult(false, null, null, "Choose a JPEG, PNG, or WebP image to upload.");
        }

        if (file.Length < MinFileBytes)
        {
            return new PhotoSaveResult(false, null, null, "The uploaded image file is empty or incomplete.");
        }

        if (file.Length > MaxFileBytes)
        {
            return new PhotoSaveResult(false, null, null, "Candidate photos must be smaller than 2 MB.");
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
        var claimedType = (file.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            return new PhotoSaveResult(false, null, null, "Upload a JPEG, PNG, or WebP image file.");
        }

        if (!string.IsNullOrEmpty(claimedType) && claimedType is not ("image/jpeg" or "image/png" or "image/webp"))
        {
            return new PhotoSaveResult(false, null, null, "Upload a valid JPEG, PNG, or WebP image.");
        }

        byte[] data;
        await using (var stream = file.OpenReadStream())
        using (var ms = new MemoryStream())
        {
            var buffer = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaxFileBytes)
                {
                    return new PhotoSaveResult(false, null, null, "Candidate photos must be smaller than 2 MB.");
                }
                ms.Write(buffer, 0, read);
            }
            data = ms.ToArray();
        }

        if (data.Length < MinFileBytes)
        {
            return new PhotoSaveResult(false, null, null, "The uploaded image file is empty or incomplete.");
        }

        var headerSample = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 512));
        if (headerSample.Contains("<script", StringComparison.OrdinalIgnoreCase)
            || headerSample.Contains("<svg", StringComparison.OrdinalIgnoreCase)
            || headerSample.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || headerSample.Contains("<?php", StringComparison.OrdinalIgnoreCase))
        {
            return new PhotoSaveResult(false, null, null, "Unsupported or unsafe image content.");
        }

        string canonicalExt;
        string canonicalType;

        if (IsValidPng(data))
        {
            if (extension != ".png" || (!string.IsNullOrEmpty(claimedType) && claimedType != "image/png"))
            {
                return new PhotoSaveResult(false, null, null, "File extension and content type must match the PNG image format.");
            }
            canonicalExt = ".png";
            canonicalType = "image/png";
        }
        else if (IsValidJpeg(data))
        {
            if (extension is not (".jpg" or ".jpeg") || (!string.IsNullOrEmpty(claimedType) && claimedType != "image/jpeg"))
            {
                return new PhotoSaveResult(false, null, null, "File extension and content type must match the JPEG image format.");
            }
            canonicalExt = ".jpg";
            canonicalType = "image/jpeg";
        }
        else if (IsValidWebP(data))
        {
            if (extension != ".webp" || (!string.IsNullOrEmpty(claimedType) && claimedType != "image/webp"))
            {
                return new PhotoSaveResult(false, null, null, "File extension and content type must match the WebP image format.");
            }
            canonicalExt = ".webp";
            canonicalType = "image/webp";
        }
        else
        {
            return new PhotoSaveResult(false, null, null, "Upload a valid JPEG, PNG, or WebP raster image.");
        }

        Directory.CreateDirectory(_baseDirectory);
        var generatedName = $"{Guid.NewGuid():N}{canonicalExt}";
        var fullPath = ResolveSafePath(generatedName);
        if (fullPath == null)
        {
            return new PhotoSaveResult(false, null, null, "Invalid storage path.");
        }

        await File.WriteAllBytesAsync(fullPath, data, cancellationToken);
        return new PhotoSaveResult(true, generatedName, canonicalType, null);
    }

    public (string FullPath, string ContentType)? TryResolveFile(string? fileName, string? storedContentType)
    {
        var fullPath = ResolveSafePath(fileName);
        if (fullPath == null || !File.Exists(fullPath))
        {
            return null;
        }

        var ext = Path.GetExtension(fileName!).ToLowerInvariant();
        var safeContentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };

        if (safeContentType == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(storedContentType)
            && !string.Equals(storedContentType, safeContentType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return (fullPath, safeContentType);
    }

    public void TryDeleteFile(string? fileName)
    {
        var fullPath = ResolveSafePath(fileName);
        if (fullPath != null && File.Exists(fullPath))
        {
            try
            {
                File.Delete(fullPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string? ResolveSafePath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !SafeFileNameRegex().IsMatch(fileName))
        {
            return null;
        }

        var baseWithSeparator = _baseDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _baseDirectory
            : _baseDirectory + Path.DirectorySeparatorChar;
        var candidatePath = Path.GetFullPath(Path.Combine(_baseDirectory, fileName));
        if (!candidatePath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return candidatePath;
    }

    private static bool IsValidPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 33) return false;
        if (!data[..8].SequenceEqual(PngHeader)) return false;
        if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R') return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension) return false;
        return data[^8..].SequenceEqual(PngIend);
    }

    private static bool IsValidJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data[0] != 0xFF || data[1] != 0xD8 || data[2] != 0xFF)
        {
            return false;
        }

        var end = data.Length;
        while (end > 2 && data[end - 1] == 0x00)
        {
            end--;
        }
        if (end < 4 || data[end - 2] != 0xFF || data[end - 1] != 0xD9)
        {
            return false;
        }

        var pos = 2;
        while (pos + 3 < end)
        {
            if (data[pos] != 0xFF)
            {
                return false;
            }
            while (pos < end && data[pos] == 0xFF)
            {
                pos++;
            }
            if (pos >= end) break;
            var marker = data[pos++];

            if (marker == 0xD9 || marker == 0xDA) // EOI or SOS
            {
                break;
            }
            if (marker == 0x00 || (marker >= 0xD0 && marker <= 0xD8))
            {
                continue;
            }
            if (pos + 1 >= end)
            {
                return false;
            }

            var segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos, 2));
            if (segLen < 2 || pos + segLen > end)
            {
                return false;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (segLen < 7) return false;
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 5, 2));
                return width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension;
            }

            pos += segLen;
        }

        return false;
    }

    private static bool IsValidWebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24) return false;
        return data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P'
            && data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8';
    }

    [GeneratedRegex(@"^[a-f0-9]{32}\.(jpg|png|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeFileNameRegex();
}
