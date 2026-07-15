// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "PPIDE",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "PPIDE",
            path: "Sources/PPIDE"
        )
    ]
)
