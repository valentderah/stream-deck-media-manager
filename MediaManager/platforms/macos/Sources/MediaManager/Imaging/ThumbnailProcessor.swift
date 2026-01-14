import Foundation
import AppKit

class ThumbnailProcessor {
    private static let targetSize = 144
    private static let partSize = 72
    
    static func processThumbnail(image: NSImage, mediaInfo: inout MediaInfo) {
        guard let croppedImage = ImageUtils.cropToSquare(image: image, targetSize: targetSize) else {
            return
        }
        
        if let base64 = ImageUtils.encodeImageToBase64(image: croppedImage, size: targetSize) {
            mediaInfo.CoverArtBase64 = base64
        }
        
        let parts = splitImageIntoParts(image: croppedImage, sourceSize: targetSize)
        if parts.count >= 4 {
            mediaInfo.CoverArtPart1Base64 = parts[0]
            mediaInfo.CoverArtPart2Base64 = parts[1]
            mediaInfo.CoverArtPart3Base64 = parts[2]
            mediaInfo.CoverArtPart4Base64 = parts[3]
        }
    }
    
    private static func splitImageIntoParts(image: NSImage, sourceSize: Int) -> [String] {
        var parts: [String] = []
        
        guard let tiffData = image.tiffRepresentation,
              let sourceBitmapRep = NSBitmapImageRep(data: tiffData) else {
            return []
        }
        
        for row in 0..<2 {
            for col in 0..<2 {
                let sourceX = col * partSize
                let sourceY = row * partSize
                
                let partImage = NSImage(size: NSSize(width: partSize, height: partSize))
                partImage.lockFocus()
                
                let sourceRect = NSRect(
                    x: CGFloat(sourceX),
                    y: CGFloat(sourceY),
                    width: CGFloat(partSize),
                    height: CGFloat(partSize)
                )
                
                let destRect = NSRect(
                    x: 0,
                    y: 0,
                    width: CGFloat(partSize),
                    height: CGFloat(partSize)
                )
                
                sourceBitmapRep.draw(
                    in: destRect,
                    from: sourceRect,
                    operation: .sourceOver,
                    fraction: 1.0,
                    respectFlipped: false,
                    hints: nil
                )
                
                partImage.unlockFocus()
                
                if let base64 = ImageUtils.encodeImageToBase64(image: partImage, size: partSize) {
                    parts.append(base64)
                } else {
                    parts.append("")
                }
            }
        }
        
        return parts
    }
}
