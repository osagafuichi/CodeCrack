import SwiftUI
import AppKit
import Highlightr

/// A code editor backed by `NSTextView` (Option 3), highlighted by Highlightr's
/// `CodeAttributedString` — an `NSTextStorage` subclass that re-highlights as you type
/// using highlight.js (185 languages). Gives us a line-number gutter, native undo/find/
/// selection, and broad syntax highlighting.
///
/// AppKit is deliberately sealed inside this one file; the rest of the app stays pure
/// SwiftUI and talks to this through a plain SwiftUI-shaped API.
struct CodeEditor: NSViewRepresentable {
    @Binding var text: String
    /// highlight.js language name (e.g. "python"), or nil to auto-detect.
    var language: String?
    /// When set to a 1-indexed line, scroll to and select it, then reset to nil.
    /// Used by the Issues panel's click-to-jump.
    var revealLine: Binding<Int?> = .constant(nil)

    static let font = NSFont.monospacedSystemFont(ofSize: 13, weight: .regular)

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSScrollView {
        // Build the TextKit stack around a CodeAttributedString so highlighting is automatic.
        let textStorage = CodeAttributedString()
        let layoutManager = NSLayoutManager()
        textStorage.addLayoutManager(layoutManager)
        let textContainer = NSTextContainer(size: NSSize(width: CGFloat.greatestFiniteMagnitude,
                                                         height: CGFloat.greatestFiniteMagnitude))
        layoutManager.addTextContainer(textContainer)

        let textView = NSTextView(frame: .zero, textContainer: textContainer)
        context.coordinator.textStorage = textStorage
        context.coordinator.textView = textView

        // Theme + font. Pick a theme matching the current appearance.
        let dark = textView.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        let themeName = dark ? "atom-one-dark" : "atom-one-light"
        textStorage.highlightr.setTheme(to: themeName)
        textStorage.highlightr.theme.setCodeFont(Self.font)
        context.coordinator.themeIsDark = dark

        let bg = textStorage.highlightr.theme.themeBackgroundColor ?? .textBackgroundColor
        textView.backgroundColor = bg
        textView.insertionPointColor = dark ? .white : .black
        textView.textContainerInset = NSSize(width: 8, height: 10)
        textView.delegate = context.coordinator
        textView.isEditable = true
        textView.isSelectable = true
        textView.isRichText = false
        textView.allowsUndo = true
        textView.usesFindBar = true

        // Turn off the "prose" conveniences that fight against code.
        textView.isAutomaticQuoteSubstitutionEnabled = false
        textView.isAutomaticDashSubstitutionEnabled = false
        textView.isAutomaticTextReplacementEnabled = false
        textView.isAutomaticSpellingCorrectionEnabled = false
        textView.isContinuousSpellCheckingEnabled = false
        textView.isGrammarCheckingEnabled = false

        // Non-wrapping: code scrolls horizontally instead of wrapping.
        textView.isVerticallyResizable = true
        textView.isHorizontallyResizable = true
        textView.autoresizingMask = []
        textView.minSize = .zero
        textView.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude,
                                  height: CGFloat.greatestFiniteMagnitude)
        textContainer.widthTracksTextView = false

        // Content + language (CodeAttributedString highlights on assignment).
        textStorage.language = language
        textView.string = text

        let scrollView = NSScrollView()
        scrollView.borderType = .noBorder
        scrollView.hasVerticalScroller = true
        scrollView.hasHorizontalScroller = true
        scrollView.autohidesScrollers = true
        scrollView.documentView = textView

        let ruler = LineNumberRulerView(textView: textView)
        scrollView.verticalRulerView = ruler
        scrollView.hasVerticalRuler = true
        scrollView.rulersVisible = true

        // Put the caret in the editor as soon as it's on screen. Without this the
        // freshly built NSTextView isn't the window's first responder, so opening a
        // file shows the text but keystrokes go nowhere until you click into it —
        // which reads as "the file can't be edited." Deferred because the view has
        // no window yet at make-time. Runs once per file (makeNSView is keyed by the
        // document's `.id`), so it doesn't fight the user for focus while typing.
        DispatchQueue.main.async { [weak textView] in
            guard let textView, let window = textView.window else { return }
            window.makeFirstResponder(textView)
        }

        return scrollView
    }

    func updateNSView(_ scrollView: NSScrollView, context: Context) {
        context.coordinator.parent = self
        guard let textView = scrollView.documentView as? NSTextView,
              let textStorage = context.coordinator.textStorage else { return }

        if textView.string != text {                 // file opened / external change
            let selection = textView.selectedRanges
            textView.string = text
            textView.selectedRanges = selection
        }
        if textStorage.language != language {         // switched to a different file type
            textStorage.language = language
        }

        // Click-to-jump: scroll to and select the requested 1-indexed line, then clear
        // the binding so tapping the same finding again re-fires.
        if let target = revealLine.wrappedValue {
            if let range = charRange(forLine: target, in: textView.string as NSString) {
                textView.scrollRangeToVisible(range)
                textView.setSelectedRange(range)
                textView.window?.makeFirstResponder(textView)
            }
            DispatchQueue.main.async { revealLine.wrappedValue = nil }
        }
    }

    /// The character range of the `line`-th line (1-indexed), or nil if out of bounds.
    private func charRange(forLine line: Int, in text: NSString) -> NSRange? {
        guard line >= 1 else { return nil }
        var current = 1
        var location = 0
        while current < line {
            let lineRange = text.lineRange(for: NSRange(location: location, length: 0))
            let next = lineRange.location + lineRange.length
            if next <= location { return nil }   // reached the end before the target line
            location = next
            current += 1
            if location >= text.length && current < line { return nil }
        }
        guard location <= text.length else { return nil }
        // Return the line's content range without the trailing newline for a tidy selection.
        let full = text.lineRange(for: NSRange(location: min(location, text.length), length: 0))
        var contents = full
        if contents.length > 0 {
            let end = text.rangeOfCharacter(from: .newlines,
                                            options: [.backwards],
                                            range: contents)
            if end.location != NSNotFound && end.location + end.length == contents.location + contents.length {
                contents.length -= end.length
            }
        }
        return contents
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: CodeEditor
        weak var textView: NSTextView?
        weak var textStorage: CodeAttributedString?
        var themeIsDark = false

        init(_ parent: CodeEditor) { self.parent = parent }

        func textDidChange(_ notification: Notification) {
            guard let textView = notification.object as? NSTextView else { return }
            parent.text = textView.string
        }
    }
}
