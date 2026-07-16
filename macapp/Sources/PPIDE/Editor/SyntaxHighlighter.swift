import AppKit

/// Languages we ship token rules for. Anything else renders as `.plain`.
enum CodeLanguage: Equatable {
    case swift, python, javascript, java, json, markdown, plain

    /// Map a file extension to a language. Unknown extensions -> `.plain`.
    static func detect(fileExtension ext: String) -> CodeLanguage {
        switch ext.lowercased() {
        case "swift":                              return .swift
        case "py", "pyw":                          return .python
        case "js", "jsx", "ts", "tsx", "mjs", "cjs": return .javascript
        case "java":                               return .java
        case "json":                               return .json
        case "md", "markdown":                     return .markdown
        default:                                   return .plain
        }
    }
}

/// Token palette. System colors so the editor adapts to light/dark automatically.
private enum Palette {
    static let keyword = NSColor.systemPurple
    static let string  = NSColor.systemRed
    static let comment = NSColor.systemGreen
    static let number  = NSColor.systemBlue
    static let type    = NSColor.systemTeal
    static let plain   = NSColor.labelColor
}

/// One regex -> color mapping. Rules are applied in order; later rules override earlier
/// ones on overlap, so strings and comments must come last to win over keywords inside them.
private struct Rule {
    let regex: NSRegularExpression
    let color: NSColor

    init(_ pattern: String, _ color: NSColor, options: NSRegularExpression.Options = []) {
        // Patterns are compile-time constants, so a failure here is a programmer error.
        self.regex = try! NSRegularExpression(pattern: pattern, options: options)
        self.color = color
    }
}

enum SyntaxHighlighter {

    /// Repaint `storage` in place for `language`. Only foreground colors change, so the
    /// caret and selection are untouched.
    static func apply(to storage: NSTextStorage, language: CodeLanguage, font: NSFont) {
        let full = NSRange(location: 0, length: storage.length)
        storage.beginEditing()
        storage.setAttributes([.font: font, .foregroundColor: Palette.plain], range: full)
        let text = storage.string
        for rule in rules(for: language) {
            rule.regex.enumerateMatches(in: text, options: [], range: full) { match, _, _ in
                guard let r = match?.range, r.length > 0 else { return }
                storage.addAttribute(.foregroundColor, value: rule.color, range: r)
            }
        }
        storage.endEditing()
    }

    // MARK: - Rule sets

    private static func rules(for language: CodeLanguage) -> [Rule] {
        switch language {
        case .swift:      return swiftRules
        case .python:     return pythonRules
        case .javascript: return jsRules
        case .java:       return javaRules
        case .json:       return jsonRules
        case .markdown:   return markdownRules
        case .plain:      return []
        }
    }

    private static func keywords(_ words: [String]) -> String {
        "\\b(?:" + words.joined(separator: "|") + ")\\b"
    }

    // Shared literal patterns.
    private static let number   = "\\b\\d+(?:\\.\\d+)?\\b"
    private static let dqString = "\"(?:[^\"\\\\\\n]|\\\\.)*\""
    private static let sqString = "'(?:[^'\\\\\\n]|\\\\.)*'"
    private static let lineSlash = "//[^\\n]*"
    private static let lineHash  = "#[^\\n]*"
    private static let blockComment = "/\\*[\\s\\S]*?\\*/"
    private static let capType   = "\\b[A-Z][A-Za-z0-9_]*\\b"

    private static let swiftRules: [Rule] = [
        Rule(capType, Palette.type),
        Rule(keywords([
            "func", "let", "var", "if", "else", "guard", "for", "while", "return", "class",
            "struct", "enum", "protocol", "extension", "import", "switch", "case", "default",
            "break", "continue", "in", "do", "try", "catch", "throw", "throws", "rethrows",
            "async", "await", "nil", "true", "false", "self", "Self", "init", "deinit",
            "static", "private", "public", "internal", "fileprivate", "open", "final", "lazy",
            "weak", "unowned", "mutating", "nonmutating", "some", "any", "where", "as", "is",
            "repeat", "defer", "typealias", "associatedtype", "inout", "subscript", "willSet",
            "didSet", "get", "set", "convenience", "required", "override", "indirect",
        ]), Palette.keyword),
        Rule(number, Palette.number),
        Rule(dqString, Palette.string),
        Rule(lineSlash, Palette.comment),
        Rule(blockComment, Palette.comment),
    ]

    private static let pythonRules: [Rule] = [
        Rule(capType, Palette.type),
        Rule(keywords([
            "def", "class", "return", "if", "elif", "else", "for", "while", "import", "from",
            "as", "try", "except", "finally", "raise", "with", "lambda", "yield", "None",
            "True", "False", "and", "or", "not", "in", "is", "pass", "break", "continue",
            "global", "nonlocal", "del", "assert", "async", "await", "match", "case",
        ]), Palette.keyword),
        Rule(number, Palette.number),
        Rule("\"\"\"[\\s\\S]*?\"\"\"", Palette.string),
        Rule("'''[\\s\\S]*?'''", Palette.string),
        Rule(dqString, Palette.string),
        Rule(sqString, Palette.string),
        Rule(lineHash, Palette.comment),
    ]

    private static let jsRules: [Rule] = [
        Rule(capType, Palette.type),
        Rule(keywords([
            "const", "let", "var", "function", "return", "if", "else", "for", "while", "class",
            "extends", "import", "export", "from", "default", "new", "this", "typeof",
            "instanceof", "async", "await", "try", "catch", "finally", "throw", "switch",
            "case", "break", "continue", "null", "undefined", "true", "false", "void", "yield",
            "delete", "in", "of", "do", "interface", "type", "enum", "implements", "public",
            "private", "protected", "readonly", "static", "get", "set", "super",
        ]), Palette.keyword),
        Rule(number, Palette.number),
        Rule("`(?:[^`\\\\]|\\\\.)*`", Palette.string),
        Rule(dqString, Palette.string),
        Rule(sqString, Palette.string),
        Rule(lineSlash, Palette.comment),
        Rule(blockComment, Palette.comment),
    ]

    private static let javaRules: [Rule] = [
        Rule(capType, Palette.type),
        Rule(keywords([
            "public", "private", "protected", "class", "interface", "enum", "extends",
            "implements", "abstract", "final", "static", "void", "int", "long", "short",
            "byte", "char", "boolean", "float", "double", "new", "return", "if", "else",
            "for", "while", "do", "switch", "case", "default", "break", "continue", "try",
            "catch", "finally", "throw", "throws", "import", "package", "this", "super",
            "instanceof", "null", "true", "false", "synchronized", "volatile", "transient",
            "native", "strictfp", "assert", "var", "record", "sealed", "permits", "yield",
        ]), Palette.keyword),
        Rule(number, Palette.number),
        Rule(dqString, Palette.string),
        Rule(sqString, Palette.string),
        Rule(lineSlash, Palette.comment),
        Rule(blockComment, Palette.comment),
    ]

    private static let jsonRules: [Rule] = [
        Rule(keywords(["true", "false", "null"]), Palette.keyword),
        Rule(number, Palette.number),
        Rule(dqString, Palette.string),
    ]

    private static let markdownRules: [Rule] = [
        Rule("^#{1,6}\\s.*$", Palette.keyword, options: [.anchorsMatchLines]), // headers
        Rule("`[^`\\n]*`", Palette.string),          // inline code
        Rule("\\*\\*[^*\\n]+\\*\\*", Palette.type),  // bold
    ]
}
