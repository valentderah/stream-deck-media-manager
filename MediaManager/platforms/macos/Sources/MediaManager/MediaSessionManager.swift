import Foundation
import MediaPlayer
import AppKit

class MediaSessionManager {
    private static var updateTimer: Timer?
    private static let updateSemaphore = DispatchSemaphore(value: 1)
    private static let debounceDelay: TimeInterval = 0.1
    private static var debounceTimer: Timer?
    private static let debounceLock = NSLock()
    
    static func run() {
        setupMediaPlayerNotifications()
        DispatchQueue.global(qos: .userInitiated).async {
            readCommands()
        }
        
        RunLoop.current.run()
    }
    
    private static func setupMediaPlayerNotifications() {
        let distributedCenter = DistributedNotificationCenter.default()
        
        distributedCenter.addObserver(
            forName: NSNotification.Name("com.apple.iTunes.playerInfo"),
            object: nil,
            queue: .main
        ) { _ in
            debouncedUpdate()
        }

        distributedCenter.addObserver(
            forName: NSNotification.Name("com.spotify.client.PlaybackStateChanged"),
            object: nil,
            queue: .main
        ) { _ in
            debouncedUpdate()
        }
        

        updateTimer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { _ in
            if MPNowPlayingInfoCenter.default().nowPlayingInfo != nil {
                debouncedUpdate()
            }
        }
        RunLoop.current.add(updateTimer!, forMode: .common)

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
            updateCurrentMediaInfo()
        }
    }
    
    private static func debouncedUpdate() {
        debounceLock.lock()
        debounceTimer?.invalidate()
        debounceTimer = Timer.scheduledTimer(withTimeInterval: debounceDelay, repeats: false) { _ in
            updateCurrentMediaInfo()
        }
        debounceLock.unlock()
    }
    
    private static func readCommands() {
        while let line = readLine() {
            let command = line.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            
            switch command {
            case "toggle":
                togglePlayPause()
            case "next":
                nextTrack()
            case "previous":
                previousTrack()
            case "update":
                updateCurrentMediaInfo()
            default:
                break
            }
        }
    }
    
    private static func updateCurrentMediaInfo() {
        updateSemaphore.wait()
        defer { updateSemaphore.signal() }
        
        let mediaInfo = getCurrentMediaInfo()
        
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = []
            let jsonData = try encoder.encode(mediaInfo)
            if let jsonString = String(data: jsonData, encoding: .utf8) {
                print(jsonString)
                fflush(stdout)
            }
        } catch {
            let errorMessage = "Error encoding media info: \(error.localizedDescription)"
            fputs(errorMessage + "\n", stderr)
        }
    }
    
    private static func getCurrentMediaInfo() -> MediaInfo {
        var info = MediaInfo()
        
        let nowPlayingInfo = MPNowPlayingInfoCenter.default().nowPlayingInfo
        
        guard let nowPlaying = nowPlayingInfo else {
            return info
        }
        
        info.Title = nowPlaying[MPMediaItemPropertyTitle] as? String ?? ""
        info.Artist = nowPlaying[MPMediaItemPropertyArtist] as? String ?? ""
        info.AlbumTitle = nowPlaying[MPMediaItemPropertyAlbumTitle] as? String ?? ""
        info.AlbumArtist = nowPlaying[MPMediaItemPropertyAlbumArtist] as? String ?? ""
        
        if !info.Artist.isEmpty {
            let artists = info.Artist.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }
            info.Artists = artists.filter { !$0.isEmpty }
        }
        
        if let playbackRate = nowPlaying[MPNowPlayingInfoPropertyPlaybackRate] as? NSNumber {
            if playbackRate.doubleValue > 0 {
                info.Status = "Playing"
            } else {
                info.Status = "Paused"
            }
        } else {
            info.Status = "Stopped"
        }
        
        if let artwork = nowPlaying[MPMediaItemPropertyArtwork] as? MPMediaItemArtwork {
            if let image = artwork.image(at: CGSize(width: 144, height: 144)) {
                ThumbnailProcessor.processThumbnail(image: image, mediaInfo: &info)
            }
        }
        
        AppIconProcessor.getAppIconBase64(mediaInfo: &info)
        
        return info
    }
    
    private static func togglePlayPause() {
        let nowPlayingInfo = MPNowPlayingInfoCenter.default().nowPlayingInfo
        
        if let playbackRate = nowPlayingInfo?[MPNowPlayingInfoPropertyPlaybackRate] as? NSNumber,
           playbackRate.doubleValue > 0 {
            executeAppleScript("tell application \"System Events\" to key code 49")
        } else {
            executeAppleScript("tell application \"System Events\" to key code 49")
        }
    }
    
    private static func nextTrack() {
        executeAppleScript("tell application \"System Events\" to key code 124 using {command down}")
    }
    
    private static func previousTrack() {
        executeAppleScript("tell application \"System Events\" to key code 123 using {command down}")
    }
    
    private static func executeAppleScript(_ script: String) {
        if let appleScript = NSAppleScript(source: script) {
            var error: NSDictionary?
            appleScript.executeAndReturnError(&error)
            if let error = error {
                fputs("AppleScript error: \(error)\n", stderr)
            }
        }
    }
}
