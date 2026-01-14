import Foundation
import AppKit

class ImageUtils {
    static func encodeImageToBase64(image: NSImage, size: Int) -> String? {
        let imageSize = image.size
        let targetSizeFloat = CGFloat(size)
        let needsResize = abs(imageSize.width - targetSizeFloat) > 0.1 || abs(imageSize.height - targetSizeFloat) > 0.1
        
        let imageToEncode: NSImage
        if needsResize {
            guard let resized = resizeImage(image: image, targetSize: CGSize(width: size, height: size)) else {
                return nil
            }
            imageToEncode = resized
        } else {
            imageToEncode = image
        }
        
        guard let tiffData = imageToEncode.tiffRepresentation,
              let bitmapRep = NSBitmapImageRep(data: tiffData) else {
            return nil
        }
        
        guard let pngData = bitmapRep.representation(using: .png, properties: [:]) else {
            return nil
        }
        
        return pngData.base64EncodedString()
    }
    
    static func resizeImage(image: NSImage, targetSize: CGSize) -> NSImage? {
        let sourceSize = image.size
        let widthRatio = targetSize.width / sourceSize.width
        let heightRatio = targetSize.height / sourceSize.height
        let scaleFactor = min(widthRatio, heightRatio)
        
        let scaledWidth = sourceSize.width * scaleFactor
        let scaledHeight = sourceSize.height * scaleFactor
        
        let scaledSize = CGSize(width: scaledWidth, height: scaledHeight)
        
        let newImage = NSImage(size: scaledSize)
        newImage.lockFocus()
        
        image.draw(
            in: NSRect(origin: .zero, size: scaledSize),
            from: NSRect(origin: .zero, size: sourceSize),
            operation: .sourceOver,
            fraction: 1.0
        )
        
        newImage.unlockFocus()
        
        return newImage
    }
    
    static func cropToSquare(image: NSImage, targetSize: Int) -> NSImage? {
        guard let resized = resizeImage(image: image, targetSize: CGSize(width: targetSize, height: targetSize)) else {
            return nil
        }
        
        let sourceSize = resized.size
        let minDimension = min(sourceSize.width, sourceSize.height)
        
        if abs(minDimension - CGFloat(targetSize)) < 0.001 {
            return resized
        }
        
        let cropRect = NSRect(
            x: (sourceSize.width - CGFloat(targetSize)) / 2,
            y: (sourceSize.height - CGFloat(targetSize)) / 2,
            width: CGFloat(targetSize),
            height: CGFloat(targetSize)
        )
        
        let croppedImage = NSImage(size: NSSize(width: targetSize, height: targetSize))
        croppedImage.lockFocus()
        
        resized.draw(
            in: NSRect(origin: .zero, size: NSSize(width: targetSize, height: targetSize)),
            from: cropRect,
            operation: .sourceOver,
            fraction: 1.0
        )
        
        croppedImage.unlockFocus()
        
        return croppedImage
    }
    
    static func convertIconToBase64(icon: NSImage, size: Int) -> String? {
        return encodeImageToBase64(image: icon, size: size)
    }
}
