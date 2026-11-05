namespace AIHub;

internal static class LocalFile
{
    // Antivirus/indexing can briefly retain a just-written settings file on
    // Windows. Keep replacement atomic and bounded; never clear permissions.
    internal static void Replace(string prepared, string destination)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(prepared, destination, true); return; }
            catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
            { Thread.Sleep(30 << attempt); }
        }
    }
}
