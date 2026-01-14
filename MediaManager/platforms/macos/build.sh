#!/bin/bash

set -e

echo "Building MediaManager for macOS..."
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if ! command -v swift &> /dev/null; then
    echo "ERROR: Swift is not installed or not in PATH"
    exit 1
fi

echo "Building Swift package..."

# Try swift build first, if it fails, use direct swiftc compilation
if ! swift build -c release 2>/dev/null; then
    echo "swift build failed, trying direct swiftc compilation..."
    
    # Get SDK path
    SDK_PATH=$(xcrun --show-sdk-path --sdk macosx 2>/dev/null)
    if [ -z "$SDK_PATH" ]; then
        SDK_PATH="/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk"
    fi
    
    # Create build directory
    mkdir -p .build/release
    
    # Compile directly with swiftc
    swiftc -sdk "$SDK_PATH" \
           -target x86_64-apple-macosx10.15 \
           -O \
           -o .build/release/MediaManager \
           Sources/MediaManager/main.swift \
           Sources/MediaManager/MediaInfo.swift \
           Sources/MediaManager/MediaSessionManager.swift \
           Sources/MediaManager/Imaging/AppIconProcessor.swift \
           Sources/MediaManager/Imaging/ImageUtils.swift \
           Sources/MediaManager/Imaging/ThumbnailProcessor.swift
    
    if [ $? -ne 0 ]; then
        echo ""
        echo "Build failed!"
        exit 1
    fi
fi

if [ $? -ne 0 ]; then
    echo ""
    echo "Build failed!"
    exit 1
fi

echo ""
echo "Build successful!"
echo ""


BUILD_PATH=".build/release/MediaManager"
TARGET_DIR="$SCRIPT_DIR/../../../ru.valentderah.current-media.sdPlugin/bin"
TARGET_PATH="$TARGET_DIR/MediaManager"

if [ ! -f "$BUILD_PATH" ]; then
    echo "ERROR: Built file not found at: $BUILD_PATH"
    echo "Please check the build output above for errors."
    exit 1
fi

if [ ! -d "$TARGET_DIR" ]; then
    mkdir -p "$TARGET_DIR"
fi

echo "Copying MediaManager to plugin bin folder..."
cp "$BUILD_PATH" "$TARGET_PATH"

if [ $? -ne 0 ]; then
    echo "ERROR: Failed to copy file to: $TARGET_PATH"
    exit 1
fi

chmod +x "$TARGET_PATH"

if [ ! -f "$TARGET_PATH" ]; then
    echo "ERROR: File was not copied successfully!"
    exit 1
fi

echo ""
echo "Done! MediaManager copied to plugin bin folder."
echo "File location: $TARGET_PATH"
