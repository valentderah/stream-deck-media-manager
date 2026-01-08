using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MediaManager.Windows;

class Program
{
    static async Task Main(string[] args)
    {
        await RunMediaListener();
    }

    static async Task RunMediaListener()
    {
        var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        sessionManager.CurrentSessionChanged += (s, e) => OnSessionChanged(s);

        OnSessionChanged(sessionManager);

        while (true)
        {
            var command = await Console.In.ReadLineAsync();
            if (string.IsNullOrEmpty(command)) continue;

            switch (command.Trim().ToLower())
            {
                case "toggle":
                    await TogglePlayPauseAsync();
                    break;
                case "next":
                    await NextTrackAsync();
                    break;
                case "previous":
                    await PreviousTrackAsync();
                    break;
            }
        }
    }

    private static GlobalSystemMediaTransportControlsSession? _currentSession;

    private static void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _currentSession = manager.GetCurrentSession();

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }

        UpdateCurrentMediaInfo();
    }

    private static void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, PlaybackInfoChangedEventArgs args)
    {
        UpdateCurrentMediaInfo();
    }

    private static void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession session, MediaPropertiesChangedEventArgs args)
    {
        UpdateCurrentMediaInfo();
    }

    private static async void UpdateCurrentMediaInfo()
    {
        var mediaInfo = await GetCurrentMediaInfoAsync();
        var json = JsonSerializer.Serialize(mediaInfo, MediaInfoJsonContext.Default.MediaInfo);
        
        await Console.Out.WriteLineAsync(json);
    }

    static async Task<MediaInfo> GetCurrentMediaInfoAsync()
    {
        var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        
        var activeSession = FindBestMediaSession(sessionManager);

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
                var decoder = await BitmapDecoder.CreateAsync(thumbnailStream);
                
                const int targetSize = 144;
                var originalWidth = decoder.PixelWidth;
                var originalHeight = decoder.PixelHeight;
                var aspectRatio = (double)originalWidth / originalHeight;
                
                uint scaledWidth, scaledHeight;
                int offsetX = 0;
                int offsetY = 0;
                
                if (aspectRatio > 1.0)
                {
                    scaledWidth = (uint)targetSize;
                    scaledHeight = (uint)Math.Round(targetSize / aspectRatio);
                }
                else
                {
                    scaledHeight = (uint)targetSize;
                    scaledWidth = (uint)Math.Round(targetSize * aspectRatio);
                    offsetX = (targetSize - (int)scaledWidth) / 2;
                }
                
                var transform = new BitmapTransform
                {
                    ScaledWidth = scaledWidth,
                    ScaledHeight = scaledHeight,
                    InterpolationMode = BitmapInterpolationMode.Linear
                };
                
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb
                );
                
                var scaledPixelBytes = pixelData.DetachPixelData();
                var finalPixels = new byte[targetSize * targetSize * 4];
                
                for (int y = 0; y < targetSize; y++)
                {
                    for (int x = 0; x < targetSize; x++)
                    {
                        var targetIndex = (y * targetSize + x) * 4;
                        
                        if (x >= offsetX && x < offsetX + scaledWidth && 
                            y >= offsetY && y < offsetY + scaledHeight)
                        {
                            var sourceX = x - offsetX;
                            var sourceY = y - offsetY;
                            var sourceIndex = (sourceY * (int)scaledWidth + sourceX) * 4;
                            
                            finalPixels[targetIndex] = scaledPixelBytes[sourceIndex];
                            finalPixels[targetIndex + 1] = scaledPixelBytes[sourceIndex + 1];
                            finalPixels[targetIndex + 2] = scaledPixelBytes[sourceIndex + 2];
                            finalPixels[targetIndex + 3] = scaledPixelBytes[sourceIndex + 3];
                        }
                        else
                        {
                            finalPixels[targetIndex] = 0;
                            finalPixels[targetIndex + 1] = 0;
                            finalPixels[targetIndex + 2] = 0;
                            finalPixels[targetIndex + 3] = 255;
                        }
                    }
                }
                
                using var outputStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Premultiplied,
                    (uint)targetSize,
                    (uint)targetSize,
                    96.0,
                    96.0,
                    finalPixels
                );
                await encoder.FlushAsync();
                
                outputStream.Seek(0);
                var outputBuffer = new global::Windows.Storage.Streams.Buffer((uint)outputStream.Size);
                await outputStream.ReadAsync(outputBuffer, (uint)outputStream.Size, InputStreamOptions.None);
                
                var bytes = outputBuffer.ToArray();
                info.CoverArtBase64 = Convert.ToBase64String(bytes);
            }
            catch
            {
            }
        }

        return info;
    }

    private static GlobalSystemMediaTransportControlsSession? FindBestMediaSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var currentSession = manager.GetCurrentSession();
        if (currentSession != null)
        {
            var playbackInfo = currentSession.GetPlaybackInfo();
            if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return currentSession;
            }
        }

        var allSessions = manager.GetSessions();
        var playingSession = allSessions.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        if (playingSession != null)
        {
            return playingSession;
        }

        return currentSession ?? allSessions.FirstOrDefault();
    }

    static async Task<GlobalSystemMediaTransportControlsSession?> GetActiveSessionAsync()
    {
        var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return FindBestMediaSession(sessionManager);
    }

    static async Task TogglePlayPauseAsync()
    {
        try
        {
            var activeSession = await GetActiveSessionAsync();
            if (activeSession != null)
            {
                await activeSession.TryTogglePlayPauseAsync();
            }
        }
        catch
        {
        }
    }

    static async Task NextTrackAsync()
    {
        try
        {
            var activeSession = await GetActiveSessionAsync();
            if (activeSession != null)
            {
                await activeSession.TrySkipNextAsync();
            }
        }
        catch
        {
        }
    }

    static async Task PreviousTrackAsync()
    {
        try
        {
            var activeSession = await GetActiveSessionAsync();
            if (activeSession != null)
            {
                await activeSession.TrySkipPreviousAsync();
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

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(MediaInfo))]
internal partial class MediaInfoJsonContext : JsonSerializerContext
{
}
