using System;
using System.IO;
using System.Text;

namespace CodeCrack.App.Core.Services;

public sealed class FileIO : IFileIO
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public DateTime AtomicWrite(string path, string text)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".tmp");

        File.WriteAllText(temp, text, Utf8NoBom);
        try
        {
            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        return File.GetLastWriteTimeUtc(path);
    }

    public DateTime GetLastWriteUtc(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
}
