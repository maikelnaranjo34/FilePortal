namespace FilePortal.Services;

internal static class FileStateLock
{
    public static FileStream Acquire(string lockPath)
    {
        // The same lock also protects overlapping worker processes during a deployment.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(50);
            }
        }
    }
}
