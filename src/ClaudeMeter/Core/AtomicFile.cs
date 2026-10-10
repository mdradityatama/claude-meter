namespace ClaudeMeter.Core;

/// <summary>Write-then-replace in the target's folder, so readers see the old or the new file, never half of one.</summary>
internal static class AtomicFile
{
    private const int SharingViolation = unchecked((int)0x80070020);

    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                stream.Write(content);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(full))
                        File.Replace(temp, full, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    else
                        File.Move(temp, full);
                    return;
                }
                catch (IOException ex) when (ex.HResult == SharingViolation && attempt < 3)
                {
                    Thread.Sleep(50);   // a scanner or reader has the file open for a moment
                }
            }
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
