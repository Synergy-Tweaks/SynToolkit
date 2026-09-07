#nullable enable

using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace SynToolkit.Services.AudioMixer
{
    public static class MediaImageLoader
    {
        public static async Task<ImageSource?> LoadAsync(byte[]? bytes)
        {
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
    }
}
