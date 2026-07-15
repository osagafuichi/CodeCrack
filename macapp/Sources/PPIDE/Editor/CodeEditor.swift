import SwiftUI
import AppKit

/// A code editor backed by `NSTextView` (Option 3): gives us a real line-number gutter,
/// TextKit layout that scales to large files, native undo/find/selection, and syntax
/// highlighting — none of which SwiftUI's `TextEditor` exposes today.
///
/// AppKit is deliberately sealed inside this one file; the rest of the app stays pure
/// SwiftUI and talks to this through a plain SwiftUI-shaped API. Swapping in a future
/// pure-SwiftUI text engine means rewriting only this file.
struct CodeEditor: NSViewRepresentable {
    @Binding var text: String
    var language: CodeLanguage

    static let font = NSFont.monospacedSystemFont(ofSize: 13, weight: .regular)

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSScrollView {
        let scrollView = NSScrollView()
        scrollView.borderType = .noBorder
        scrollView.hasVerticalScroller = true
        scrollView.hasHorizontalScroller = true
        scrollView.autohidesScrollers = true

        let textView = NSTextView()
        textView.delegate = context.coordinator
        textView.font = Self.font
        textView.isRichText = false
        textView.allowsUndo = true
        textView.usesFindBar = true
        textView.backgroundColor = .textBackgroundColor
        textView.textColor = .labelColor
        textView.insertionPointColor = .labelColor
        textView.textContainerInset = NSSize(width: 4, height: 6)

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
        if let container = textView.textContainer {
            container.widthTracksTextView = false
            container.containerSize = NSSize(width: CGFloat.greatestFiniteMagnitude,
                                             height: CGFloat.greatestFiniteMagnitude)
        }

        textView.string = text
        scrollView.documentView = textView

        let ruler = LineNumberRulerView(textView: textView)
        scrollView.verticalRulerView = ruler
        scrollView.hasVerticalRuler = true
        scrollView.rulersVisible = true

        context.coordinator.textView = textView
        if let storage = textView.textStorage {
            SyntaxHighlighter.apply(to: storage, language: language, font: Self.font)
        }
        return scrollView
    }

    func updateNSView(_ scrollView: NSScrollView, context: Context) {
        context.coordinator.parent = self
        guard let textView = scrollView.documentView as? NSTextView else { return }

        var needsHighlight = false
        if textView.string != text {                 // file opened / external change
            let selection = textView.selectedRanges
            textView.string = text
            textView.selectedRanges = selection
            needsHighlight = true
        }
        if context.coordinator.language != language { // switched to a different file type
            context.coordinator.language = language
            needsHighlight = true
        }
        if needsHighlight, let storage = textView.textStorage {
            SyntaxHighlighter.apply(to: storage, language: language, font: Self.font)
        }
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: CodeEditor
        var language: CodeLanguage
        weak var textView: NSTextView?

        init(_ parent: CodeEditor) {
            self.parent = parent
            self.language = parent.language
        }

        func textDidChange(_ notification: Notification) {
            guard let textView = notification.object as? NSTextView else { return }
            parent.text = textView.string
            if let storage = textView.textStorage {
                let selection = textView.selectedRanges
                SyntaxHighlighter.apply(to: storage, language: parent.language,
                                        font: CodeEditor.font)
                textView.selectedRanges = selection
            }
        }
    }
}
