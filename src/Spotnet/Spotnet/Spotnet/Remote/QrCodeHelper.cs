using System;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Spotnet.Remote;

public static class QrCodeHelper
{
    public static BitmapSource GenerateQrCodeBitmap(string content, int pixelsPerModule = 10)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeBytes = qrCode.GetGraphic(pixelsPerModule);

        var image = new BitmapImage();
        using var stream = new MemoryStream(qrCodeBytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }
}
