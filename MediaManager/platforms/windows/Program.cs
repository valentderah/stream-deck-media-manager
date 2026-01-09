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
        // Запускаем основной цикл прослушивания событий
        await RunMediaListener();
    }

    static async Task RunMediaListener()
    {
        var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        // Подписываемся на смену медиа-сессии (например, переключились с Spotify на YouTube)
        sessionManager.CurrentSessionChanged += (s, e) => OnSessionChanged(s);

        // Получаем и обрабатываем текущую сессию при запуске
        OnSessionChanged(sessionManager);

        // Цикл для прослушивания команд из stdin
        while (true)
        {
            try
            {
                var command = await Console.In.ReadLineAsync();
                
                // Если stdin закрыт, выходим из цикла
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
                }
            }
            catch (IOException)
            {
                // stdin закрыт или произошла ошибка ввода/вывода
                break;
            }
            catch
            {
                // Другие ошибки игнорируем и продолжаем работу
                continue;
            }
        }
    }

    private static GlobalSystemMediaTransportControlsSession? _currentSession;

    private static void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        // Отписываемся от событий старой сессии
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        // Используем FindBestMediaSession для согласованности с GetCurrentMediaInfoAsync
        _currentSession = FindBestMediaSession(manager);

        // Подписываемся на события новой сессии
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }

        // Принудительно обновляем информацию (fire-and-forget с обработкой ошибок)
        _ = UpdateCurrentMediaInfoAsync().ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                // Игнорируем ошибки обновления медиа-информации
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, PlaybackInfoChangedEventArgs args)
    {
        // Fire-and-forget с обработкой ошибок
        _ = UpdateCurrentMediaInfoAsync().ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                // Игнорируем ошибки обновления медиа-информации
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession session, MediaPropertiesChangedEventArgs args)
    {
        // Fire-and-forget с обработкой ошибок
        _ = UpdateCurrentMediaInfoAsync().ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                // Игнорируем ошибки обновления медиа-информации
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static async Task UpdateCurrentMediaInfoAsync()
    {
        try
        {
            var mediaInfo = await GetCurrentMediaInfoAsync();
            var json = JsonSerializer.Serialize(mediaInfo, MediaInfoJsonContext.Default.MediaInfo);
            
            // Отправляем JSON в stdout. Добавляем разделитель новой строки для парсинга на стороне TS.
            await Console.Out.WriteLineAsync(json);
        }
        catch
        {
            // Игнорируем ошибки при выводе, чтобы не крашить приложение
        }
    }

    static async Task<MediaInfo> GetCurrentMediaInfoAsync()
    {
        var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        
        // Улучшенный поиск активной сессии
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
                using var thumbnailStream = await mediaProperties.Thumbnail.OpenReadAsync();
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

                // Разделяем изображение на 4 части (2x2)
                const int partSize = targetSize / 2; // 144x144 для каждой части
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
                                var sourceIndex = (sourceY * targetSize + sourceX) * 4;
                                var targetIndex = (y * partSize + x) * 4;
                                
                                if (sourceIndex < finalPixels.Length && targetIndex < partPixels.Length)
                                {
                                    partPixels[targetIndex] = finalPixels[sourceIndex];
                                    partPixels[targetIndex + 1] = finalPixels[sourceIndex + 1];
                                    partPixels[targetIndex + 2] = finalPixels[sourceIndex + 2];
                                    partPixels[targetIndex + 3] = finalPixels[sourceIndex + 3];
                                }
                            }
                        }

                        using var partStream = new InMemoryRandomAccessStream();
                        var partEncoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, partStream);
                        partEncoder.SetPixelData(
                            BitmapPixelFormat.Rgba8,
                            BitmapAlphaMode.Premultiplied,
                            (uint)partSize,
                            (uint)partSize,
                            96.0,
                            96.0,
                            partPixels
                        );
                        await partEncoder.FlushAsync();

                        partStream.Seek(0);
                        var partBuffer = new global::Windows.Storage.Streams.Buffer((uint)partStream.Size);
                        await partStream.ReadAsync(partBuffer, (uint)partStream.Size, InputStreamOptions.None);
                        var partBytes = partBuffer.ToArray();
                        parts.Add(partBytes);
                    }
                }

                // Присваиваем части: 0=topLeft, 1=topRight, 2=bottomLeft, 3=bottomRight
                if (parts.Count >= 4)
                {
                    info.CoverArtPart1Base64 = Convert.ToBase64String(parts[0]); // Top Left
                    info.CoverArtPart2Base64 = Convert.ToBase64String(parts[1]); // Top Right
                    info.CoverArtPart3Base64 = Convert.ToBase64String(parts[2]); // Bottom Left
                    info.CoverArtPart4Base64 = Convert.ToBase64String(parts[3]); // Bottom Right
                }
            }
            catch
            {
            }
        }

        return info;
    }

    private static GlobalSystemMediaTransportControlsSession? FindBestMediaSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        // 1. Сначала пытаемся получить сессию, которую система считает текущей
        var currentSession = manager.GetCurrentSession();
        if (currentSession != null)
        {
            var playbackInfo = currentSession.GetPlaybackInfo();
            // 2. Если она активна - отлично, это наш клиент
            if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return currentSession;
            }
        }

        // 3. Если текущая сессия неактивна, ищем любую другую играющую сессию
        var allSessions = manager.GetSessions();
        var playingSession = allSessions.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        if (playingSession != null)
        {
            return playingSession;
        }

        // 4. Если играющих нет, возвращаем "текущую" (даже если она на паузе) или первую попавшуюся
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
    public string CoverArtPart1Base64 { get; set; } = string.Empty; // Top Left
    public string CoverArtPart2Base64 { get; set; } = string.Empty; // Top Right
    public string CoverArtPart3Base64 { get; set; } = string.Empty; // Bottom Left
    public string CoverArtPart4Base64 { get; set; } = string.Empty; // Bottom Right
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(MediaInfo))]
internal partial class MediaInfoJsonContext : JsonSerializerContext
{
}
