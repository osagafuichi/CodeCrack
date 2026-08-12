using System;
using System.Collections.Generic;
using CodeCrack.App.Core.Services;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeFileIO : IFileIO
{
    public Dictionary<string, string> Files { get; } = new();
    public DateTime NextWriteUtc { get; set; } = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

    public string Read(string path) => Files.TryGetValue(path, out var t) ? t : "";
    public DateTime AtomicWrite(string path, string text) { Files[path] = text; return NextWriteUtc; }
    public DateTime GetLastWriteUtc(string path) => NextWriteUtc;
}
