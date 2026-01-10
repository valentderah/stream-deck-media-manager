using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace MediaManager.Windows;

class MediaSessionManager
{
    private static readonly SemaphoreSlim _updateSemaphore = new SemaphoreSlim(1, 1);
    private static GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private static readonly HashSet<string> _subscribedSessions = new HashSet<string>();
    private static GlobalSystemMediaTransportControlsSession? _lastActiveSession;
    private static Timer? _updateDebounceTimer;
    private static readonly object _debounceLock = new object();

    public static async Task RunAsync()
    {
        var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _sessionManager = sessionManager;
        sessionManager.CurrentSessionChanged += (s, e) => OnSessionChanged(s);
        sessionManager.SessionsChanged += (s, e) => OnSessionsChanged(s);
        SubscribeToAllSessions(sessionManager);

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
                        await UpdateCurrentMediaInfoAsync();
                        break;
                }
            }
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error in command loop: {ex.Message}");
                continue;
            }
        }
    }

    private static void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        SubscribeToAllSessions(manager);
    }

    private static void SubscribeToSession(GlobalSystemMediaTransportControlsSession session)
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to subscribe to session: {ex.Message}");
        }
    }

    private static void SubscribeToAllSessions(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            var allSessions = manager.GetSessions();
            foreach (var session in allSessions)
            {
                SubscribeToSession(session);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to subscribe to all sessions: {ex.Message}");
        }
    }

    private static void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        DebouncedUpdate(100);
    }

    private static void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, PlaybackInfoChangedEventArgs args)
    {
        DebouncedUpdate(100);
    }

    private static void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession session, MediaPropertiesChangedEventArgs args)
    {
        DebouncedUpdate(100);
    }

    private static void DebouncedUpdate(int delayMs)
    {
        lock (_debounceLock)
        {
            _updateDebounceTimer?.Dispose();
            _updateDebounceTimer = new Timer(_ =>
            {
                _ = UpdateCurrentMediaInfoAsync();
            }, null, delayMs, Timeout.Infinite);
        }
    }

    private static async Task UpdateCurrentMediaInfoAsync()
    {
        await _updateSemaphore.WaitAsync();

        try
        {
            var mediaInfo = await GetCurrentMediaInfoAsync();
            var json = JsonSerializer.Serialize(mediaInfo, MediaInfoJsonContext.Default.MediaInfo);
            await Console.Out.WriteLineAsync(json);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error updating media info: {ex.Message}");
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
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error getting media properties: {ex.Message}");
            }

            try
            {
                playbackInfo = activeSession.GetPlaybackInfo();
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error getting playback info: {ex.Message}");
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
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"Error parsing artists: {ex.Message}");
                }
            }

            var title = mediaProperties?.Title ?? string.Empty;
            var artist = mediaProperties?.Artist ?? string.Empty;

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
            
            if (info.Status == "Playing")
            {
                _lastActiveSession = activeSession;
            }

            if (mediaProperties != null && mediaProperties.Thumbnail != null)
            {
                try
                {
                    await ThumbnailProcessor.ProcessThumbnailAsync(mediaProperties.Thumbnail, info);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"Error processing thumbnail: {ex.Message}");
                }
            }

            return info;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error in GetCurrentMediaInfoAsync: {ex.Message}");
            return new MediaInfo();
        }
    }

    private static GlobalSystemMediaTransportControlsSession? FindBestMediaSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            var allSessions = manager.GetSessions();
            GlobalSystemMediaTransportControlsSession? pausedLastActive = null;
            GlobalSystemMediaTransportControlsSession? pausedCurrent = null;
            GlobalSystemMediaTransportControlsSession? anyPaused = null;
            
            // 1. Абсолютный приоритет: любая играющая сессия.
            foreach (var session in allSessions)
            {
                try
                {
                    var playbackInfo = session.GetPlaybackInfo();
                    if (playbackInfo == null) continue;
                    
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        return session; // Нашли играющую, немедленно возвращаем.
                    }
                    
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    {
                        // Собираем кандидатов на случай, если играющих нет
                        if (_lastActiveSession != null && session.SourceAppUserModelId == _lastActiveSession.SourceAppUserModelId)
                        {
                            pausedLastActive = session;
                        }
                        var currentSystemSession = manager.GetCurrentSession();
                        if (currentSystemSession != null && session.SourceAppUserModelId == currentSystemSession.SourceAppUserModelId)
                        {
                            pausedCurrent = session;
                        }
                        if (anyPaused == null)
                        {
                            anyPaused = session;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error finding best session: {ex.Message}");
                }
            }

            // 2. Если играющих нет, возвращаем в порядке приоритета "на паузе".
            return pausedLastActive 
                ?? pausedCurrent 
                ?? anyPaused 
                ?? allSessions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Critical error in FindBestMediaSession: {ex.Message}");
            return null;
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
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error toggling play/pause: {ex.Message}");
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
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error skipping next: {ex.Message}");
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
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error skipping previous: {ex.Message}");
        }
    }
}
