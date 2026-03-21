using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ASD4G.Helpers;

public static class ExecutableIconHelper
{
    public static ImageSource? LoadIcon(string executablePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(20, 20));

            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
