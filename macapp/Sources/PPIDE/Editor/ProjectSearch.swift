import Foundation

/// One search match: a file, a 1-based line number, and the trimmed line text.
struct SearchHit: Identifiable, Hashable {
    let id = UUID()
    let url: URL
    let line: Int
    let preview: String
}

/// Project-wide text search and replace over the files under a root folder.
/// Foundation only; skips hidden files and anything that isn't valid UTF-8 text.
enum ProjectSearch {
    static let maxHits = 500

    static func search(_ query: String, in root: URL) -> [SearchHit] {
        guard !query.isEmpty else { return [] }
        var hits: [SearchHit] = []
        for url in textFiles(under: root) {
            if hits.count >= maxHits { break }
            guard let content = try? String(contentsOf: url, encoding: .utf8) else { continue }
            var lineNo = 0
            content.enumerateLines { line, stop in
                lineNo += 1
                if line.range(of: query, options: .caseInsensitive) != nil {
                    hits.append(SearchHit(url: url, line: lineNo,
                                          preview: line.trimmingCharacters(in: .whitespaces)))
                    if hits.count >= maxHits { stop = true }
                }
            }
        }
        return hits
    }

    /// Replace every (case-sensitive) occurrence of `query` with `replacement` across the
    /// project. Returns the URLs of files that changed.
    @discardableResult
    static func replaceAll(_ query: String, with replacement: String, in root: URL) -> [URL] {
        guard !query.isEmpty else { return [] }
        var changed: [URL] = []
        for url in textFiles(under: root) {
            guard let content = try? String(contentsOf: url, encoding: .utf8),
                  content.contains(query) else { continue }
            let updated = content.replacingOccurrences(of: query, with: replacement)
            if updated != content, (try? updated.write(to: url, atomically: true, encoding: .utf8)) != nil {
                changed.append(url)
            }
        }
        return changed
    }

    private static func textFiles(under root: URL) -> [URL] {
        let fm = FileManager.default
        guard let enumerator = fm.enumerator(
            at: root,
            includingPropertiesForKeys: [.isRegularFileKey],
            options: [.skipsHiddenFiles]
        ) else { return [] }

        var files: [URL] = []
        for case let url as URL in enumerator {
            if (try? url.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true {
                files.append(url)
            }
        }
        return files
    }
}
