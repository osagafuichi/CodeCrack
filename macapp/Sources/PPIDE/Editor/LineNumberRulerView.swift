import AppKit

/// A gutter that draws line numbers alongside an `NSTextView`, kept in sync with scrolling
/// and edits. Non-wrapping editor -> one fragment per line, so numbering is straightforward.
final class LineNumberRulerView: NSRulerView {

    private weak var textView: NSTextView?

    init(textView: NSTextView) {
        self.textView = textView
        super.init(scrollView: textView.enclosingScrollView, orientation: .verticalRuler)
        clientView = textView
        ruleThickness = 46

        let nc = NotificationCenter.default
        nc.addObserver(self, selector: #selector(invalidate),
                       name: NSText.didChangeNotification, object: textView)
        nc.addObserver(self, selector: #selector(invalidate),
                       name: NSView.frameDidChangeNotification, object: textView)
        if let clip = scrollView?.contentView {
            clip.postsBoundsChangedNotifications = true
            nc.addObserver(self, selector: #selector(invalidate),
                           name: NSView.boundsDidChangeNotification, object: clip)
        }
    }

    required init(coder: NSCoder) { fatalError("init(coder:) is not used") }

    @objc private func invalidate() { needsDisplay = true }

    override func drawHashMarksAndLabels(in rect: NSRect) {
        guard let textView,
              let layoutManager = textView.layoutManager,
              let container = textView.textContainer else { return }

        // Gutter background (match the editor) + hairline separator.
        (textView.backgroundColor).setFill()
        bounds.fill()
        NSColor.separatorColor.setFill()
        NSRect(x: bounds.maxX - 1, y: bounds.minY, width: 1, height: bounds.height).fill()

        let content = textView.string as NSString
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedSystemFont(ofSize: 11, weight: .regular),
            .foregroundColor: NSColor.secondaryLabelColor,
        ]

        let visibleRect = textView.visibleRect
        let glyphRange = layoutManager.glyphRange(forBoundingRect: visibleRect, in: container)
        let charRange = layoutManager.characterRange(forGlyphRange: glyphRange, actualGlyphRange: nil)

        // Line number for the first visible character = 1 + newlines before it.
        var lineNumber = 1
        if charRange.location > 0 {
            var count = 0
            content.enumerateSubstrings(in: NSRange(location: 0, length: charRange.location),
                                        options: [.byLines, .substringNotRequired]) { _, _, _, _ in
                count += 1
            }
            lineNumber = count + 1
        }

        let relativeY = convert(NSPoint.zero, from: textView).y
        let inset = textView.textContainerInset.height

        var index = charRange.location
        let end = NSMaxRange(charRange)
        while index <= end {
            let lineRange = content.lineRange(for: NSRange(location: index, length: 0))
            let glyphLineRange = layoutManager.glyphRange(forCharacterRange: lineRange,
                                                          actualCharacterRange: nil)
            var effective = NSRange()
            let lineRect = layoutManager.lineFragmentRect(forGlyphAt: glyphLineRange.location,
                                                          effectiveRange: &effective,
                                                          withoutAdditionalLayout: true)

            let numString = "\(lineNumber)" as NSString
            let size = numString.size(withAttributes: attrs)
            let y = lineRect.minY + relativeY + inset + (lineRect.height - size.height) / 2
            numString.draw(at: NSPoint(x: ruleThickness - size.width - 8, y: y),
                           withAttributes: attrs)

            lineNumber += 1
            let next = NSMaxRange(lineRange)
            if next <= index { break }   // guard against zero-length final line
            index = next
        }
    }
}
