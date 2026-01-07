using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MediaManager.Windows;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "toggle")
        {
            await TogglePlayPauseAsync();
            return;
        }

        try
        {
            var mediaInfo = await GetCurrentMediaInfoAsync();
            var json = JsonSerializer.Serialize(mediaInfo, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            Console.WriteLine(json);
        }
        catch (Exception)
        {
            var emptyInfo = new MediaInfo();
            var json = JsonSerializer.Serialize(emptyInfo, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            Console.WriteLine(json);
        }
    }

    static async Task<MediaInfo> GetCurrentMediaInfoAsync()
    {
        var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var sessions = sessionManager.GetSessions();

        GlobalSystemMediaTransportControlsSession? activeSession = null;
        foreach (var session in sessions)
        {
            var sessionPlaybackInfo = session.GetPlaybackInfo();
            if (sessionPlaybackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ||
                sessionPlaybackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
            {
                activeSession = session;
                break;
            }
        }

        activeSession ??= sessions.FirstOrDefault();

        if (activeSession == null)
        {
            return new MediaInfo();
        }

        var mediaProperties = await activeSession.TryGetMediaPropertiesAsync();
        var playbackInfo = activeSession.GetPlaybackInfo();

        var artists = new List<string>();
        if (!string.IsNullOrEmpty(mediaProperties?.Artist))
        {
            var artistParts = mediaProperties.Artist.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            artists.AddRange(artistParts.Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)));
        }

        var info = new MediaInfo
        {
            Title = mediaProperties?.Title ?? string.Empty,
            Artist = mediaProperties?.Artist ?? string.Empty,
            Artists = artists,
            AlbumArtist = mediaProperties?.AlbumArtist ?? string.Empty,
            AlbumTitle = mediaProperties?.AlbumTitle ?? string.Empty,
            Status = playbackInfo.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "Playing",
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "Paused",
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "Stopped",
                _ => "Stopped"
            }
        };

        if (mediaProperties?.Thumbnail != null)
        {
            try
            {
                var thumbnailStream = await mediaProperties.Thumbnail.OpenReadAsync();
                var buffer = new global::Windows.Storage.Streams.Buffer((uint)thumbnailStream.Size);
                await thumbnailStream.ReadAsync(buffer, (uint)thumbnailStream.Size, InputStreamOptions.None);
                
                var bytes = buffer.ToArray();
                info.CoverArtBase64 = Convert.ToBase64String(bytes);
            }
            catch
            {
            }
        }

        return info;
    }

    static async Task TogglePlayPauseAsync()
    {
        try
        {
            var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = sessionManager.GetSessions();

            GlobalSystemMediaTransportControlsSession? activeSession = null;
            foreach (var session in sessions)
            {
                var sessionPlaybackInfo = session.GetPlaybackInfo();
                if (sessionPlaybackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ||
                    sessionPlaybackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                {
                    activeSession = session;
                    break;
                }
            }

            activeSession ??= sessions.FirstOrDefault();

            if (activeSession != null)
            {
                await activeSession.TryTogglePlayPauseAsync();
            }
        }
        catch
        {
        }
    }
}

class MediaInfo
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public List<string> Artists { get; set; } = new List<string>();
    public string AlbumArtist { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "Stopped";
    public string CoverArtBase64 { get; set; } = string.Empty;
}

