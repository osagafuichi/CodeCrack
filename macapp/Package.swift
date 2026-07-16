// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "PPIDE",
    platforms: [.macOS(.v14)],
    dependencies: [
        .package(url: "https://github.com/raspu/Highlightr.git", from: "2.1.0")
    ],
    targets: [
        .executableTarget(
            name: "PPIDE",
            dependencies: ["Highlightr"],
            path: "Sources/PPIDE",
            swiftSettings: [.swiftLanguageMode(.v5)]
        )
    ]
)
