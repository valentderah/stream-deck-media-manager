using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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
        if (width == height && width == targetSize)
        {
            var result = new byte[targetSize * targetSize * 4];
            Array.Copy(sourcePixelBytes, result, result.Length);
            return result;
        }

        var minDimension = Math.Min(width, height);
        var scale = (double)targetSize / minDimension;

        var scaledWidth = (uint)Math.Round(width * scale);
        var scaledHeight = (uint)Math.Round(height * scale);

        var offsetX = scaledWidth > targetSize ? (int)((scaledWidth - targetSize) / 2) : 0;
        var offsetY = scaledHeight > targetSize ? (int)((scaledHeight - targetSize) / 2) : 0;

        if (width == height && Math.Abs(scale - 1.0) < 0.001 && offsetX == 0 && offsetY == 0)
        {
            var result = new byte[targetSize * targetSize * 4];
            Array.Copy(sourcePixelBytes, result, result.Length);
            return result;
        }

        var scaledPixels = new byte[scaledWidth * scaledHeight * 4];

        for (uint y = 0; y < scaledHeight; y++)
        {
            for (uint x = 0; x < scaledWidth; x++)
            {
                var srcX = x / scale;
                var srcY = y / scale;

                var x1 = (uint)Math.Floor(srcX);
                var y1 = (uint)Math.Floor(srcY);
                var x2 = (uint)Math.Min(x1 + 1, width - 1);
                var y2 = (uint)Math.Min(y1 + 1, height - 1);

                var fx = srcX - x1;
                var fy = srcY - y1;

                var p11 = GetPixel(sourcePixelBytes, width, x1, y1);
                var p21 = GetPixel(sourcePixelBytes, width, x2, y1);
                var p12 = GetPixel(sourcePixelBytes, width, x1, y2);
                var p22 = GetPixel(sourcePixelBytes, width, x2, y2);

                var p = InterpolatePixels(p11, p21, p12, p22, fx, fy);

                var targetIndex = (y * scaledWidth + x) * 4;
                scaledPixels[targetIndex] = p.R;
                scaledPixels[targetIndex + 1] = p.G;
                scaledPixels[targetIndex + 2] = p.B;
                scaledPixels[targetIndex + 3] = p.A;
            }
        }

        var finalPixels = new byte[targetSize * targetSize * 4];

        for (int y = 0; y < targetSize; y++)
        {
            for (int x = 0; x < targetSize; x++)
            {
                var srcX = x + offsetX;
                var srcY = y + offsetY;

                var targetIndex = (y * targetSize + x) * 4;

                if (srcX < scaledWidth && srcY < scaledHeight)
                {
                    var sourceIndex = (srcY * scaledWidth + srcX) * 4;
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

    private static (byte R, byte G, byte B, byte A) GetPixel(byte[] pixels, uint width, uint x, uint y)
    {
        var index = (y * width + x) * 4;
        if (index + 3 < pixels.Length)
        {
            return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
        }
        return (0, 0, 0, 255);
    }

    private static (byte R, byte G, byte B, byte A) InterpolatePixels(
        (byte R, byte G, byte B, byte A) p11,
        (byte R, byte G, byte B, byte A) p21,
        (byte R, byte G, byte B, byte A) p12,
        (byte R, byte G, byte B, byte A) p22,
        double fx, double fy)
    {
        var r = (byte)Math.Round(
            p11.R * (1 - fx) * (1 - fy) +
            p21.R * fx * (1 - fy) +
            p12.R * (1 - fx) * fy +
            p22.R * fx * fy
        );
        var g = (byte)Math.Round(
            p11.G * (1 - fx) * (1 - fy) +
            p21.G * fx * (1 - fy) +
            p12.G * (1 - fx) * fy +
            p22.G * fx * fy
        );
        var b = (byte)Math.Round(
            p11.B * (1 - fx) * (1 - fy) +
            p21.B * fx * (1 - fy) +
            p12.B * (1 - fx) * fy +
            p22.B * fx * fy
        );
        var a = (byte)Math.Round(
            p11.A * (1 - fx) * (1 - fy) +
            p21.A * fx * (1 - fy) +
            p12.A * (1 - fx) * fy +
            p22.A * fx * fy
        );
        return (r, g, b, a);
    }

    public static async Task<string> ConvertIconToBase64Async(string exePath, int size)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon == null)
            {
                return string.Empty;
            }

            using var bitmap = new Bitmap(icon.ToBitmap(), size, size);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var width = bitmap.Width;
                var height = bitmap.Height;
                var stride = bitmapData.Stride;

                var bgraBytes = new byte[stride * height];
                Marshal.Copy(bitmapData.Scan0, bgraBytes, 0, bgraBytes.Length);

                var rgbaBytes = new byte[width * height * 4];

                for (int y = 0; y < height; y++)
                {
                    int srcRowOffset = y * stride;
                    int dstRowOffset = y * width * 4;

                    for (int x = 0; x < width; x++)
                    {
                        int srcIndex = srcRowOffset + (x * 4);
                        int dstIndex = dstRowOffset + (x * 4);

                        rgbaBytes[dstIndex] = bgraBytes[srcIndex + 2];
                        rgbaBytes[dstIndex + 1] = bgraBytes[srcIndex + 1];
                        rgbaBytes[dstIndex + 2] = bgraBytes[srcIndex];
                        rgbaBytes[dstIndex + 3] = bgraBytes[srcIndex + 3];
                    }
                }

                return await EncodeImageToBase64Async(rgbaBytes, width);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        catch
        {
            return string.Empty;
        }
    }
}
