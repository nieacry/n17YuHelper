using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ZXing;

namespace N17YuHelper.Tools;

/// <summary>QR codes and barcodes in an image (ZXing).</summary>
public static class QrReader
{
    public sealed record Code(string Text, string Format)
    {
        public bool IsLink => Uri.TryCreate(Text, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
    }

    public static Code? TryRead(Bitmap image)
    {
        try
        {
            using var bmp = image.PixelFormat == PixelFormat.Format24bppRgb ? null : image.Clone(new Rectangle(Point.Empty, image.Size), PixelFormat.Format24bppRgb);
            var src = bmp ?? image;
            var data = src.LockBits(new Rectangle(Point.Empty, src.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte[] bytes;
            try
            {
                // No row padding — ZXing expects a dense array.
                int row = src.Width * 3;
                bytes = new byte[row * src.Height];
                for (int y = 0; y < src.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * row, row);
            }
            finally
            {
                src.UnlockBits(data);
            }

            var reader = new BarcodeReaderGeneric { AutoRotate = true };
            reader.Options.TryHarder = true;
            reader.Options.TryInverted = true;
            var result = reader.Decode(new RGBLuminanceSource(bytes, src.Width, src.Height, RGBLuminanceSource.BitmapFormat.BGR24));
            return result is null || string.IsNullOrEmpty(result.Text) ? null : new Code(result.Text, result.BarcodeFormat.ToString());
        }
        catch (Exception ex)
        {
            Log.Error("QR read", ex);
            return null;
        }
    }
}
