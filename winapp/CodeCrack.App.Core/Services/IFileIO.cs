namespace CodeCrack.App.Core.Services;

public interface IFileIO
{
    string Read(string path);
    DateTime AtomicWrite(string path, string text); // UTF-8 no BOM; returns new LastWriteUtc.
}
