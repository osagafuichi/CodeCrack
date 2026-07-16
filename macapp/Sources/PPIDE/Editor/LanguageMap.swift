import Foundation

/// Maps a file extension to a highlight.js language identifier used by Highlightr.
/// Returns `nil` for unknown extensions, which lets Highlightr auto-detect the language.
enum LanguageMap {
    static func name(forExtension ext: String) -> String? {
        switch ext.lowercased() {
        case "swift":                       return "swift"
        case "py", "pyw", "pyi":            return "python"
        case "js", "jsx", "mjs", "cjs":     return "javascript"
        case "ts", "tsx":                   return "typescript"
        case "java":                        return "java"
        case "kt", "kts":                   return "kotlin"
        case "c", "h":                      return "c"
        case "cpp", "cc", "cxx", "hpp", "hh": return "cpp"
        case "cs":                          return "csharp"
        case "go":                          return "go"
        case "rs":                          return "rust"
        case "rb":                          return "ruby"
        case "php":                         return "php"
        case "swiftpm", "json", "jsonc":    return "json"
        case "md", "markdown":              return "markdown"
        case "html", "htm", "xhtml":        return "xml"
        case "xml", "plist", "storyboard", "xib", "svg": return "xml"
        case "css":                         return "css"
        case "scss":                        return "scss"
        case "less":                        return "less"
        case "sh", "bash", "zsh":           return "bash"
        case "yml", "yaml":                 return "yaml"
        case "toml", "ini", "cfg", "conf":  return "ini"
        case "sql":                         return "sql"
        case "dart":                        return "dart"
        case "scala", "sc":                 return "scala"
        case "lua":                         return "lua"
        case "r":                           return "r"
        case "pl", "pm":                    return "perl"
        case "m":                           return "objectivec"
        case "mm":                          return "objectivec"
        case "gradle", "groovy":            return "groovy"
        case "dockerfile":                  return "dockerfile"
        case "makefile", "mk":              return "makefile"
        case "vim":                         return "vim"
        case "diff", "patch":               return "diff"
        default:                            return nil   // let Highlightr auto-detect
        }
    }
}
