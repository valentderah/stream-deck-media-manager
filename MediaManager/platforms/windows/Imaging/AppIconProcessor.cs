using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Media.Control;

namespace MediaManager.Windows.Imaging;

static class AppIconProcessor
{
    private const int IconSize = 32;
    private static readonly ConcurrentDictionary<string, string> _iconCache = new();

    private static string? FindExecutablePath(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
            if (processes.Length > 0)
            {
                var process = processes[0];
                try
                {
                    var exePath = process.MainModule?.FileName;
                    process.Dispose();
                    return exePath;
                }
                catch
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // ignored
        }
        return null;
    }


    public static async Task<string> GetAppIconBase64Async(string appUserModelId, dynamic? sourceAppInfo)
    {
        if (string.IsNullOrEmpty(appUserModelId))
        {
            return string.Empty;
        }

        if (_iconCache.TryGetValue(appUserModelId, out var cachedIcon))
        {
            return cachedIcon;
        }

        try
        {
            if (sourceAppInfo != null)
            {
                try
                {
                    var displayInfo = sourceAppInfo?.DisplayInfo;
                    if (displayInfo != null)
                    {
                        var logoStreamRef = displayInfo.GetLogo(new global::Windows.Foundation.Size(IconSize, IconSize));
                        if (logoStreamRef != null)
                        {
                            using var stream = await logoStreamRef.OpenReadAsync();
                            if (stream != null && stream.Size > 0)
                            {
                                var decoder = await BitmapDecoder.CreateAsync(stream!);
                                var transform = new BitmapTransform
                                {
                                    ScaledWidth = IconSize,
                                    ScaledHeight = IconSize,
                                    InterpolationMode = BitmapInterpolationMode.Linear
                                };
                                var pixelData = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
                                var pixels = pixelData.DetachPixelData();
                                var result = await ImageUtils.EncodeImageToBase64Async(pixels, IconSize);

                                _iconCache.TryAdd(appUserModelId, result);
                                return result;
                            }
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }

            var packageManager = new global::Windows.Management.Deployment.PackageManager();
            var packageFamilyName = appUserModelId.Split('!').FirstOrDefault();

            if (string.IsNullOrEmpty(packageFamilyName))
            {
                return string.Empty;
            }

            var packages = packageManager.FindPackagesForUser(string.Empty, packageFamilyName);

            if (!packages.Any())
            {
                try
                {
                    var exePath = FindExecutablePath(appUserModelId);
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        try
                        {
                            var result = await ImageUtils.ConvertIconToBase64Async(exePath, IconSize);
                            if (!string.IsNullOrEmpty(result))
                            {
                                _iconCache.TryAdd(appUserModelId, result);
                                return result;
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                return string.Empty;
            }

            var package = packages.First();
            var appListEntries = await package.GetAppListEntriesAsync();
            var entry = appListEntries.FirstOrDefault(e => e.AppUserModelId == appUserModelId);

            if (entry == null)
            {
                return string.Empty;
            }

            var logo = entry.DisplayInfo.GetLogo(new global::Windows.Foundation.Size(IconSize, IconSize));
            if (logo != null)
            {
                using var stream = await logo.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);

                var transform = new BitmapTransform
                {
                    ScaledWidth = IconSize,
                    ScaledHeight = IconSize,
                    InterpolationMode = BitmapInterpolationMode.Linear
                };

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb
                );

                var pixels = pixelData.DetachPixelData();
                var result = await ImageUtils.EncodeImageToBase64Async(pixels, IconSize);
                _iconCache.TryAdd(appUserModelId, result);
                return result;
            }
        }
        catch
        {
            // ignored
        }

        return string.Empty;
    }
}