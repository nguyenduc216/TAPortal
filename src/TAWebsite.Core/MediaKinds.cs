using System.Text.RegularExpressions;

namespace TAWebsite.Core;

/// <summary>The three groups of the website media library (Admin → Website TA → Media).</summary>
public enum MediaKind { Image, Video, File }

// What dbo.WebsiteMedia may hold (migration 066): images (5 MB), MP4 videos (25 MB, below the 30 MB IIS default request
// limit), PDF files (10 MB) and YouTube links (no bytes, ContentType "video/youtube", ExternalUrl). Files are recognised by
// their first bytes, never by the name or the browser's content type.
public static partial class MediaKinds
{
    public const string YouTube = "video/youtube";
    public const string Mp4 = "video/mp4";
    public const string Pdf = "application/pdf";
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public const int MaxVideoBytes = 25 * 1024 * 1024;
    public const int MaxPdfBytes = 10 * 1024 * 1024;
    /// <summary>Largest file the public site keeps in memory; bigger ones (videos) are read from SQL per request.</summary>
    public const int CacheLimit = MaxPdfBytes;

    public static readonly IReadOnlyList<string> ImageTypes = ["image/png", "image/jpeg", "image/webp", "image/gif"];

    public static MediaKind KindOf(string contentType) =>
        contentType is Mp4 or YouTube ? MediaKind.Video : contentType == Pdf ? MediaKind.File : MediaKind.Image;

    /// <summary>Content types stored for a group (used to filter the library).</summary>
    public static IReadOnlyList<string> TypesOf(MediaKind kind) => kind switch
    {
        MediaKind.Video => [Mp4, YouTube],
        MediaKind.File => [Pdf],
        _ => ImageTypes
    };

    public static int MaxBytes(string contentType) => contentType switch { Mp4 => MaxVideoBytes, Pdf => MaxPdfBytes, _ => MaxImageBytes };

    /// <summary>"application/pdf" for a PDF, "video/mp4" for an MP4 (ISO base media "ftyp" box), otherwise null.</summary>
    public static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 5 && data[0] == (byte)'%' && data[1] == (byte)'P' && data[2] == (byte)'D' && data[3] == (byte)'F' && data[4] == (byte)'-') return Pdf;
        if (data.Length >= 12 && data[4] == (byte)'f' && data[5] == (byte)'t' && data[6] == (byte)'y' && data[7] == (byte)'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(data.Slice(8, 4));
            // QuickTime (.mov) and 3GP containers are not reliably playable in every browser: only MP4/M4V brands.
            if (brand is "isom" or "iso2" or "iso4" or "iso5" or "iso6" or "mp41" or "mp42" or "avc1" or "M4V " or "dash" or "mmp4") return Mp4;
        }
        return null;
    }

    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?(?:youtube\.com/(?:watch\?(?:.*&)?v=|shorts/|embed/|live/)|youtu\.be/)([A-Za-z0-9_-]{11})(?:[?&#/].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeUrl();

    /// <summary>The 11-character video id of a YouTube link (watch, youtu.be, shorts, embed, live), or null.</summary>
    public static string? YouTubeId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500) return null;
        var m = YouTubeUrl().Match(url.Trim());
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>The canonical link stored for a YouTube video.</summary>
    public static string YouTubeWatchUrl(string id) => "https://www.youtube.com/watch?v=" + id;

    public static string YouTubeEmbedUrl(string id) => "https://www.youtube-nocookie.com/embed/" + id;

    public static string YouTubeThumbUrl(string id) => $"https://i.ytimg.com/vi/{id}/hqdefault.jpg";
}
