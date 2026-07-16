import SwiftUI
import AppKit

/// Removes the 1px separator that `NavigationSplitView` (an `NSSplitView` underneath)
/// strokes between the sidebar and detail, so the app reads as one continuous surface.
///
/// SwiftUI exposes no API for this, and painting over the divider is fragile, so we
/// reach the backing `NSSplitView` and reclass the instance to a subclass that reports
/// zero divider thickness and draws nothing. This is instance isa-swizzling
/// (`object_setClass`) — safe here because the subclass adds no stored properties, only
/// method overrides.
///
/// AppKit is sealed in this one file; the rest of the app stays pure SwiftUI.
struct SplitDividerHider: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView {
        let probe = NSView()
        DispatchQueue.main.async { Self.apply(from: probe) }
        return probe
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        DispatchQueue.main.async { Self.apply(from: nsView) }
    }

    private static func apply(from probe: NSView) {
        var view: NSView? = probe
        while let cur = view, !(cur is NSSplitView) { view = cur.superview }
        guard let split = view as? NSSplitView else { return }

        // Zero out the divider itself (isa-swizzle to a no-divider subclass) …
        if !(split is DividerlessSplitView) {
            object_setClass(split, DividerlessSplitView.self)
        }
        // … and hide the separate drop-shadow view AppKit strokes along the boundary,
        // which is what actually reads as a hairline between the panes.
        for sub in split.subviews where String(describing: type(of: sub)).contains("ShadowView") {
            sub.isHidden = true
        }
        split.needsLayout = true
        split.needsDisplay = true
    }
}

/// An `NSSplitView` that has no visible or space-consuming divider.
private final class DividerlessSplitView: NSSplitView {
    override var dividerThickness: CGFloat { 0 }
    override var dividerColor: NSColor { .clear }
    override func drawDivider(in rect: NSRect) { /* draw nothing */ }
}
