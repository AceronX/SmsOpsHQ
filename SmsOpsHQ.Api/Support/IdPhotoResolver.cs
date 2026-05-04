namespace SmsOpsHQ.Api.Support;

// Resolves optional on-disk ID scan images: {IdPhotosDirectory}/{customerKey}.jpg|jpeg|png|...
public static class IdPhotoResolver
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    public static bool TryResolvePath(string? directory, int customerKey, out string fullPath, out string contentType)
    {
        fullPath = string.Empty;
        contentType = "application/octet-stream";

        if (string.IsNullOrWhiteSpace(directory))
            return false;

        string root = Path.GetFullPath(directory.Trim());
        if (!Directory.Exists(root))
            return false;

        foreach (string ext in Extensions)
        {
            string candidate = Path.Combine(root, $"{customerKey}{ext}");
            if (!File.Exists(candidate))
                continue;

            fullPath = candidate;
            contentType = ext.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };
            return true;
        }

        return false;
    }
}
