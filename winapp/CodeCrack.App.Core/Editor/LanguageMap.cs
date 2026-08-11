using System.Collections.Generic;

namespace CodeCrack.App.Core.Editor;

/// <summary>Maps a bare file extension (no dot) to a TextMate scope name, or null if unknown.</summary>
public static class LanguageMap
{
    private static readonly IReadOnlyDictionary<string, string> Scopes = new Dictionary<string, string>
    {
        ["swift"] = "source.swift",
        ["py"] = "source.python", ["pyw"] = "source.python", ["pyi"] = "source.python",
        ["js"] = "source.js", ["jsx"] = "source.js.jsx", ["mjs"] = "source.js", ["cjs"] = "source.js",
        ["ts"] = "source.ts", ["tsx"] = "source.tsx",
        ["java"] = "source.java",
        ["kt"] = "source.kotlin", ["kts"] = "source.kotlin",
        ["c"] = "source.c", ["h"] = "source.c",
        ["cpp"] = "source.cpp", ["cc"] = "source.cpp", ["cxx"] = "source.cpp",
        ["hpp"] = "source.cpp", ["hh"] = "source.cpp",
        ["cs"] = "source.cs",
        ["go"] = "source.go",
        ["rs"] = "source.rust",
        ["rb"] = "source.ruby",
        ["php"] = "source.php",
        ["json"] = "source.json", ["jsonc"] = "source.json.comments",
        ["md"] = "text.html.markdown", ["markdown"] = "text.html.markdown",
        ["html"] = "text.html.basic", ["htm"] = "text.html.basic", ["xhtml"] = "text.html.basic",
        ["xml"] = "text.xml", ["plist"] = "text.xml", ["svg"] = "text.xml",
        ["css"] = "source.css", ["scss"] = "source.css.scss", ["less"] = "source.css.less",
        ["sh"] = "source.shell", ["bash"] = "source.shell", ["zsh"] = "source.shell",
        ["yml"] = "source.yaml", ["yaml"] = "source.yaml",
        ["toml"] = "source.toml", ["ini"] = "source.ini", ["cfg"] = "source.ini", ["conf"] = "source.ini",
        ["sql"] = "source.sql",
        ["dart"] = "source.dart",
        ["scala"] = "source.scala", ["sc"] = "source.scala",
        ["lua"] = "source.lua",
        ["r"] = "source.r",
        ["pl"] = "source.perl", ["pm"] = "source.perl",
        ["groovy"] = "source.groovy", ["gradle"] = "source.groovy",
        ["dockerfile"] = "source.dockerfile",
        ["makefile"] = "source.makefile", ["mk"] = "source.makefile",
        ["diff"] = "source.diff", ["patch"] = "source.diff",
        ["ps1"] = "source.powershell",
    };

    public static string? ScopeForExtension(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return null;
        string key = ext.TrimStart('.').ToLowerInvariant();
        return Scopes.TryGetValue(key, out string? scope) ? scope : null;
    }
}
