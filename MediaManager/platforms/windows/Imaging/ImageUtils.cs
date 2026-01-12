using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MediaManager.Windows.Imaging;

static class ImageUtils
{
    public static async Task<string> EncodeImageToBase64Async(byte[] pixels, int size)
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

    public static byte[] CropToSquare(byte[] sourcePixelBytes, uint width, uint height, int targetSize)
    {
        var image = Image.LoadPixelData<Rgba32>(sourcePixelBytes, (int)width, (int)height);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(targetSize, targetSize),
            Mode = ResizeMode.Crop
        }));

        var croppedPixels = new byte[targetSize * targetSize * 4];
        image.CopyPixelDataTo(croppedPixels);
        return croppedPixels;
    }

    public static async Task<string> ConvertIconToBase64Async(string exePath, int size)
    {
        using var iconStream = new MemoryStream();
        using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
        {
            if (icon == null)
            {
                return string.Empty;
            }
            icon.ToBitmap().Save(iconStream, System.Drawing.Imaging.ImageFormat.Png);
        }
        iconStream.Seek(0, SeekOrigin.Begin);

        using var image = await Image.LoadAsync<Rgba32>(iconStream);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(size, size),
            Mode = ResizeMode.Crop
        }));

        var rgbaBytes = new byte[size * size * 4];
        image.CopyPixelDataTo(rgbaBytes);
        return await EncodeImageToBase64Async(rgbaBytes, size);
    }
}
