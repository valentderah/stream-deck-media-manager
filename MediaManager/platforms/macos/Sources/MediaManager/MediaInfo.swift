import Foundation

struct MediaInfo: Codable {
    var Title: String = ""
    var Artist: String = ""
    var Artists: [String] = []
    var AlbumArtist: String = ""
    var AlbumTitle: String = ""
    var Status: String = "Stopped"
    var CoverArtBase64: String = ""
    var CoverArtPart1Base64: String = ""
    var CoverArtPart2Base64: String = ""
    var CoverArtPart3Base64: String = ""
    var CoverArtPart4Base64: String = ""
    var AppIconBase64: String = ""
}
