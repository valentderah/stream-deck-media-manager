using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MediaManager.Windows.Imaging;

static class ThumbnailProcessor
{
    private const int TargetSize = 144;
    private const int PartSize = 72;

    public static async Task ProcessThumbnailAsync(IRandomAccessStreamReference thumbnail, MediaInfo info)
    {
        try
        {
            using var thumbnailStream = await thumbnail.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(thumbnailStream);

            var transform = new BitmapTransform();
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb
            );

            var sourcePixelBytes = pixelData.DetachPixelData();

            var finalPixels = ImageUtils.CropToSquare(
                sourcePixelBytes,
                decoder.PixelWidth,
                decoder.PixelHeight,
                TargetSize
            );

            info.CoverArtBase64 = await ImageUtils.EncodeImageToBase64Async(finalPixels, TargetSize);

            var parts = await SplitImageIntoPartsAsync(finalPixels, TargetSize);
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


    private static async Task<List<byte[]>> SplitImageIntoPartsAsync(byte[] sourcePixels, int sourceSize)
    {
        var parts = new List<byte[]>(4);

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                var partPixels = new byte[PartSize * PartSize * 4];

                for (int y = 0; y < PartSize; y++)
                {
                    for (int x = 0; x < PartSize; x++)
                    {
                        var sourceX = col * PartSize + x;
                        var sourceY = row * PartSize + y;
                        var sourceIndex = (sourceY * sourceSize + sourceX) * 4;
                        var targetIndex = (y * PartSize + x) * 4;

                        if (sourceIndex < sourcePixels.Length && targetIndex < partPixels.Length)
                        {
                            partPixels[targetIndex] = sourcePixels[sourceIndex];
                            partPixels[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                            partPixels[targetIndex + 2] = sourcePixels[sourceIndex + 2];
                            partPixels[targetIndex + 3] = sourcePixels[sourceIndex + 3];
                        }
                    }
                }

                var partBytes = await EncodePartToBytesAsync(partPixels, PartSize);
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
}
