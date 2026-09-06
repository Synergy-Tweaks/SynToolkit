#nullable enable

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace SynToolkit.Services.AudioMixer
{
    public static class ExecutableIconLoader
    {
        public static async Task<ImageSource?> LoadAsync(string? executablePath)
        {
            byte[]? bytes = await Task.Run(() => TryExtractPngBytes(executablePath));
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            using InMemoryRandomAccessStream stream = new();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            BitmapImage image = new();
            await image.SetSourceAsync(stream);
            return image;
        }

        private static byte[]? TryExtractPngBytes(string? executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            try
            {
                using Icon? icon = Icon.ExtractAssociatedIcon(executablePath);
                if (icon is null)
                {
                    return null;
                }

                using Bitmap bitmap = icon.ToBitmap();
                using MemoryStream memoryStream = new();
                bitmap.Save(memoryStream, ImageFormat.Png);
                return memoryStream.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}
