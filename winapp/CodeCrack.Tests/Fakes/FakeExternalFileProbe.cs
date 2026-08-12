using System;
using System.Collections.Generic;
using CodeCrack.App.Core.Services;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeExternalFileProbe : IExternalFileProbe
{
    public Dictionary<string, (DateTime Mtime, string Text, bool Exists)> Files = new();

    public bool Exists(string path) => Files.TryGetValue(path, out var f) && f.Exists;
    public DateTime LastWriteUtc(string path) => Files[path].Mtime;
    public string ReadText(string path) => Files[path].Text;
}
