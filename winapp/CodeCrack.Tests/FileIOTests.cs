using System;
using System.IO;
using System.Text;
using CodeCrack.App.Core.Services;
using Xunit;

namespace CodeCrack.Tests;

public class FileIOTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccio_" + Guid.NewGuid().ToString("N"));
    public FileIOTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void AtomicWrite_WritesUtf8WithoutBom()
    {
        var io = new FileIO();
        string path = Path.Combine(_dir, "a.py");
        io.AtomicWrite(path, "print('héllo')\n");

        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("print('héllo')\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void AtomicWrite_ReturnsFreshLastWriteUtc()
    {
        var io = new FileIO();
        string path = Path.Combine(_dir, "b.txt");
        DateTime returned = io.AtomicWrite(path, "x");
        Assert.Equal(File.GetLastWriteTimeUtc(path), returned);
        Assert.Equal(DateTimeKind.Utc, returned.Kind);
    }

    [Fact]
    public void AtomicWrite_OverwritesExistingAtomically()
    {
        var io = new FileIO();
        string path = Path.Combine(_dir, "c.txt");
        io.AtomicWrite(path, "old contents");
        io.AtomicWrite(path, "new");
        Assert.Equal("new", io.Read(path));
        // no leftover temp files
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Read_MissingFileReturnsEmpty()
        => Assert.Equal(string.Empty, new FileIO().Read(Path.Combine(_dir, "nope.txt")));
}
