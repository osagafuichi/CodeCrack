using System.IO;
using CodeCrack.App.Core.Editor;
using TextMateSharp.Grammars;

namespace CodeCrackApp.Editor;

internal static class LanguageScope
{
    public static string? ScopeForPath(RegistryOptions options, string filePath)
    {
        string ext = Path.GetExtension(filePath); // includes leading '.'
        string? scope = LanguageMap.ScopeForExtension(ext);
        if (scope is not null) return scope;

        Language? lang = options.GetLanguageByExtension(ext);
        return lang is null ? null : options.GetScopeByLanguageId(lang.Id);
    }
}
