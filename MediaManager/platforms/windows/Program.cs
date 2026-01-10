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
                        break;
                    case "next":
                        await NextTrackAsync();
                        break;
                    case "previous":
                        await PreviousTrackAsync();
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
    }

    private static void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _sessionManager = manager;
        
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _currentSession = FindBestMediaSession(manager);

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }

        _ = UpdateCurrentMediaInfoAsync();
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
        var sessionManager = _sessionManager ?? await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
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
            await ProcessThumbnailAsync(mediaProperties.Thumbnail, info);
        }

        return info;
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
