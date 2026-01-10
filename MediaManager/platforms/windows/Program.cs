using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MediaManager.Windows;

class Program
{
    private static readonly SemaphoreSlim _updateSemaphore = new SemaphoreSlim(1, 1);
    private static CancellationTokenSource? _lastUpdateCancellation;
    private static GlobalSystemMediaTransportControlsSession? _currentSession;
    private static GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private static readonly HashSet<string> _subscribedSessions = new HashSet<string>();
    private static Timer? _sessionCheckTimer;
    private static string? _lastMediaTitle;
    private static string? _lastMediaArtist;

    static async Task Main(string[] args)
    {
        await RunMediaListener();
    }

    static async Task RunMediaListener()
    {
        var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _sessionManager = sessionManager;
        sessionManager.CurrentSessionChanged += (s, e) => OnSessionChanged(s);
        sessionManager.SessionsChanged += OnSessionsChanged;
        OnSessionChanged(sessionManager);
        SubscribeToAllSessions(sessionManager);

        _sessionCheckTimer = new Timer(_ =>
        {
            _ = Task.Run(async () => await CheckAndUpdateActiveSession(sessionManager));
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        while (true)
        {
            try
            {
                var command = await Console.In.ReadLineAsync();
                
                if (command == null)
                {
                    break;
                }
                
                if (string.IsNullOrEmpty(command)) continue;

                switch (command.Trim().ToLower())
                {
                    case "toggle":
                        await TogglePlayPauseAsync();
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(300);
                            await UpdateCurrentMediaInfoAsync();
                        });
                        break;
                    case "next":
                        await NextTrackAsync();
                        _ = Task.Run(async () =>
                        {
                            for (int i = 0; i < 6; i++)
                            {
                                await Task.Delay(400 + i * 200);
                                await UpdateCurrentMediaInfoAsync();
                            }
                        });
                        break;
                    case "previous":
                        await PreviousTrackAsync();
                        _ = Task.Run(async () =>
                        {
                            for (int i = 0; i < 6; i++)
                            {
                                await Task.Delay(400 + i * 200);
                                await UpdateCurrentMediaInfoAsync();
                            }
                        });
                        break;
                    case "update":
                        _ = UpdateCurrentMediaInfoAsync();
                        break;
                }
            }
            catch (IOException)
            {
                break;
            }
            catch
            {
                continue;
            }
        }

        _sessionCheckTimer?.Dispose();
    }

    private static void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager manager, SessionsChangedEventArgs args)
    {
        SubscribeToAllSessions(manager);
        OnSessionChanged(manager);
    }

    private static void SubscribeToAllSessions(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            var allSessions = manager.GetSessions();
            foreach (var session in allSessions)
            {
                try
                {
                    var sessionId = session.SourceAppUserModelId;
                    if (!_subscribedSessions.Contains(sessionId))
                    {
                        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                        _subscribedSessions.Add(sessionId);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _sessionManager = manager;
        
        if (_currentSession != null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            }
            catch
            {
            }
        }

        _currentSession = FindBestMediaSession(manager);

        if (_currentSession != null)
        {
            try
            {
                var sessionId = _currentSession.SourceAppUserModelId;
                if (!_subscribedSessions.Contains(sessionId))
                {
                    _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _subscribedSessions.Add(sessionId);
                }
            }
            catch
            {
            }
        }

        _ = UpdateCurrentMediaInfoAsync();
    }

    private static async Task CheckAndUpdateActiveSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            var bestSession = FindBestMediaSession(manager);
            if (bestSession != _currentSession)
            {
                OnSessionChanged(manager);
                return;
            }

            if (bestSession != null && _currentSession == bestSession)
            {
                try
                {
                    var mediaProperties = await bestSession.TryGetMediaPropertiesAsync();
                    if (mediaProperties != null)
                    {
                        var currentTitle = mediaProperties.Title ?? string.Empty;
                        var currentArtist = mediaProperties.Artist ?? string.Empty;
                        
                        if ((!string.IsNullOrEmpty(currentTitle) || !string.IsNullOrEmpty(currentArtist)) &&
                            (currentTitle != _lastMediaTitle || currentArtist != _lastMediaArtist))
                        {
                            _lastMediaTitle = currentTitle;
                            _lastMediaArtist = currentArtist;
                            _ = UpdateCurrentMediaInfoAsync();
                        }
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, PlaybackInfoChangedEventArgs args)
    {
        _ = UpdateCurrentMediaInfoAsync();
    }

    private static void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession session, MediaPropertiesChangedEventArgs args)
    {
        _ = UpdateCurrentMediaInfoAsync();
    }

    private static async Task UpdateCurrentMediaInfoAsync()
    {
        _lastUpdateCancellation?.Cancel();
        _lastUpdateCancellation?.Dispose();
        _lastUpdateCancellation = new CancellationTokenSource();
        
        var cancellationToken = _lastUpdateCancellation.Token;

        if (!await _updateSemaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var mediaInfo = await GetCurrentMediaInfoAsync();
            
            if (cancellationToken.IsCancellationRequested)
                return;

            var json = JsonSerializer.Serialize(mediaInfo, MediaInfoJsonContext.Default.MediaInfo);
            
            if (cancellationToken.IsCancellationRequested)
                return;
            
            await Console.Out.WriteLineAsync(json);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            _updateSemaphore.Release();
        }
    }

    static async Task<MediaInfo> GetCurrentMediaInfoAsync()
    {
        try
        {
            var sessionManager = _sessionManager ?? await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var activeSession = FindBestMediaSession(sessionManager);

            if (activeSession == null)
            {
                return new MediaInfo();
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties? mediaProperties = null;
            GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null;

            try
            {
                mediaProperties = await activeSession.TryGetMediaPropertiesAsync();
            }
            catch
            {
            }

            try
            {
                playbackInfo = activeSession.GetPlaybackInfo();
            }
            catch
            {
            }

            if (playbackInfo == null)
            {
                return new MediaInfo();
            }

            var artists = new List<string>();
            if (mediaProperties != null && !string.IsNullOrEmpty(mediaProperties.Artist))
            {
                try
                {
                    var artistParts = mediaProperties.Artist.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    artists.AddRange(artistParts.Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)));
                }
                catch
                {
                }
            }

            var title = mediaProperties?.Title ?? string.Empty;
            var artist = mediaProperties?.Artist ?? string.Empty;

            _lastMediaTitle = title;
            _lastMediaArtist = artist;

            var info = new MediaInfo
            {
                Title = title,
                Artist = artist,
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

            if (mediaProperties != null && mediaProperties.Thumbnail != null)
            {
                try
                {
                    await ProcessThumbnailAsync(mediaProperties.Thumbnail, info);
                }
                catch
                {
                }
            }

            return info;
        }
        catch
        {
            return new MediaInfo();
        }
    }

    private static async Task ProcessThumbnailAsync(IRandomAccessStreamReference thumbnail, MediaInfo info)
    {
        try
        {
            using var thumbnailStream = await thumbnail.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(thumbnailStream);
            
            const int targetSize = 144;
            var (scaledWidth, scaledHeight, offsetX, offsetY) = CalculateScaleAndOffset(
                decoder.PixelWidth, 
                decoder.PixelHeight, 
                targetSize
            );
            
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
            var finalPixels = CreateCenteredImage(scaledPixelBytes, scaledWidth, scaledHeight, targetSize, offsetX, offsetY);
            
            info.CoverArtBase64 = await EncodeImageToBase64Async(finalPixels, targetSize);
            
            var parts = await SplitImageIntoPartsAsync(finalPixels, targetSize);
            if (parts.Count >= 4)
            {
                info.CoverArtPart1Base64 = Convert.ToBase64String(parts[0]);
                info.CoverArtPart2Base64 = Convert.ToBase64String(parts[1]);
                info.CoverArtPart3Base64 = Convert.ToBase64String(parts[2]);
                info.CoverArtPart4Base64 = Convert.ToBase64String(parts[3]);
            }
        }
        catch
        {
        }
    }

    private static (uint scaledWidth, uint scaledHeight, int offsetX, int offsetY) CalculateScaleAndOffset(
        uint originalWidth, 
        uint originalHeight, 
        int targetSize)
    {
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
        
        return (scaledWidth, scaledHeight, offsetX, offsetY);
    }

    private static byte[] CreateCenteredImage(
        byte[] scaledPixels, 
        uint scaledWidth, 
        uint scaledHeight, 
        int targetSize, 
        int offsetX, 
        int offsetY)
    {
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
                    
                    finalPixels[targetIndex] = scaledPixels[sourceIndex];
                    finalPixels[targetIndex + 1] = scaledPixels[sourceIndex + 1];
                    finalPixels[targetIndex + 2] = scaledPixels[sourceIndex + 2];
                    finalPixels[targetIndex + 3] = scaledPixels[sourceIndex + 3];
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
        
        return finalPixels;
    }

    private static async Task<string> EncodeImageToBase64Async(byte[] pixels, int size)
    {
        using var outputStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
        encoder.SetPixelData(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Premultiplied,
            (uint)size,
            (uint)size,
            96.0,
            96.0,
            pixels
        );
        await encoder.FlushAsync();
        
        outputStream.Seek(0);
        var outputBuffer = new global::Windows.Storage.Streams.Buffer((uint)outputStream.Size);
        await outputStream.ReadAsync(outputBuffer, (uint)outputStream.Size, InputStreamOptions.None);
        
        return Convert.ToBase64String(outputBuffer.ToArray());
    }

    private static async Task<List<byte[]>> SplitImageIntoPartsAsync(byte[] sourcePixels, int sourceSize)
    {
        const int partSize = 72;
        var parts = new List<byte[]>(4);

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                var partPixels = new byte[partSize * partSize * 4];
                
                for (int y = 0; y < partSize; y++)
                {
                    for (int x = 0; x < partSize; x++)
                    {
                        var sourceX = col * partSize + x;
                        var sourceY = row * partSize + y;
                        var sourceIndex = (sourceY * sourceSize + sourceX) * 4;
                        var targetIndex = (y * partSize + x) * 4;
                        
                        if (sourceIndex < sourcePixels.Length && targetIndex < partPixels.Length)
                        {
                            partPixels[targetIndex] = sourcePixels[sourceIndex];
                            partPixels[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                            partPixels[targetIndex + 2] = sourcePixels[sourceIndex + 2];
                            partPixels[targetIndex + 3] = sourcePixels[sourceIndex + 3];
                        }
                    }
                }

                var partBytes = await EncodePartToBytesAsync(partPixels, partSize);
                parts.Add(partBytes);
            }
        }

        return parts;
    }

    private static async Task<byte[]> EncodePartToBytesAsync(byte[] pixels, int size)
    {
        using var partStream = new InMemoryRandomAccessStream();
        var partEncoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, partStream);
        partEncoder.SetPixelData(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Premultiplied,
            (uint)size,
            (uint)size,
            96.0,
            96.0,
            pixels
        );
        await partEncoder.FlushAsync();

        partStream.Seek(0);
        var partBuffer = new global::Windows.Storage.Streams.Buffer((uint)partStream.Size);
        await partStream.ReadAsync(partBuffer, (uint)partStream.Size, InputStreamOptions.None);
        return partBuffer.ToArray();
    }

    private static GlobalSystemMediaTransportControlsSession? FindBestMediaSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            GlobalSystemMediaTransportControlsSession? currentSession = null;
            try
            {
                currentSession = manager.GetCurrentSession();
            }
            catch
            {
            }

            if (currentSession != null)
            {
                try
                {
                    var playbackInfo = currentSession.GetPlaybackInfo();
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        return currentSession;
                    }
                }
                catch
                {
                }
            }

            List<GlobalSystemMediaTransportControlsSession> allSessions;
            try
            {
                allSessions = manager.GetSessions().ToList();
            }
            catch
            {
                return currentSession;
            }

            var playingSessions = new List<GlobalSystemMediaTransportControlsSession>();
            foreach (var session in allSessions)
            {
                try
                {
                    var playbackInfo = session.GetPlaybackInfo();
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        playingSessions.Add(session);
                    }
                }
                catch
                {
                }
            }

            if (playingSessions.Count > 0)
            {
                if (currentSession != null && playingSessions.Contains(currentSession))
                {
                    return currentSession;
                }
                return playingSessions.FirstOrDefault();
            }

            var pausedSessions = new List<GlobalSystemMediaTransportControlsSession>();
            foreach (var session in allSessions)
            {
                try
                {
                    var playbackInfo = session.GetPlaybackInfo();
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    {
                        pausedSessions.Add(session);
                    }
                }
                catch
                {
                }
            }

            if (pausedSessions.Count > 0 && currentSession != null && pausedSessions.Contains(currentSession))
            {
                return currentSession;
            }

            if (currentSession != null)
            {
                return currentSession;
            }

            return allSessions.FirstOrDefault();
        }
        catch
        {
            try
            {
                return manager.GetCurrentSession();
            }
            catch
            {
                return null;
            }
        }
    }

    static async Task<GlobalSystemMediaTransportControlsSession?> GetActiveSessionAsync()
    {
        var sessionManager = _sessionManager ?? await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
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
    public string CoverArtPart1Base64 { get; set; } = string.Empty;
    public string CoverArtPart2Base64 { get; set; } = string.Empty;
    public string CoverArtPart3Base64 { get; set; } = string.Empty;
    public string CoverArtPart4Base64 { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(MediaInfo))]
internal partial class MediaInfoJsonContext : JsonSerializerContext
{
}
