// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MediaManager",
    platforms: [
        .macOS(.v10_15)
    ],
    products: [
        .executable(
            name: "MediaManager",
            targets: ["MediaManager"]
        )
    ],
    dependencies: [],
    targets: [
        .executableTarget(
            name: "MediaManager",
            dependencies: []
        )
    ]
)
