import Foundation
import AppKit

class AppIconProcessor {
    private static var iconCache: [String: String] = [:]
    private static let iconSize = 32
    
    static func getAppIconBase64(mediaInfo: inout MediaInfo) {
        let workspace = NSWorkspace.shared
        let runningApps = workspace.runningApplications
        
        let mediaAppIdentifiers = [
            "com.spotify.client",
            "com.apple.Music",
            "com.tidal.desktop",
            "com.vox.music",
            "com.plexsquared.Plex",
            "com.apple.QuickTimePlayerX"
        ]
        
        for app in runningApps {
            if let bundleId = app.bundleIdentifier,
               mediaAppIdentifiers.contains(bundleId) {
                if let icon = app.icon {
                    if let base64 = ImageUtils.convertIconToBase64(icon: icon, size: iconSize) {
                        mediaInfo.AppIconBase64 = base64
                        iconCache[bundleId] = base64
                        return
                    }
                }
            }
        }
        
        if let activeApp = runningApps.first(where: { $0.isActive }) {
            if let bundleId = activeApp.bundleIdentifier {
                if let cached = iconCache[bundleId] {
                    mediaInfo.AppIconBase64 = cached
                    return
                }
                
                if let icon = activeApp.icon,
                   let base64 = ImageUtils.convertIconToBase64(icon: icon, size: iconSize) {
                    mediaInfo.AppIconBase64 = base64
                    iconCache[bundleId] = base64
                    return
                }
            }
        }
    }
}
