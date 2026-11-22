namespace FilePortal.Services;

internal static class FileDeletion
{
    public static string DescribeFailure(string path, Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReadOnly | FileAttributes.Directory)) == FileAttributes.ReadOnly)
                    return "The file is read-only.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Fall back to the original access error if attributes are unavailable.
            }

            return "Windows denied FilePortal permission to delete this item.";
        }

        return (exception.HResult & 0xffff) switch
        {
            32 or 33 => "The file is open in another program. Close it and try again.",
            145 => "Some of the folder contents could not be removed.",
            _ => "Windows could not delete this item."
        };
    }
}
