using System.IO;
using QRCoder;

namespace Spotnet.Remote;

/// <summary>
/// Bouwt de koppelings-QR als PNG-bytes, met dezelfde QRCoder-instance en
/// foutcorrectie (ECC-niveau M) als de Windows-client. De bytes zijn
/// platformneutraal: Avalonia maakt er een Bitmap van, WPF een BitmapImage.
/// Heet RemoteQrCode en niet QrCodeHelper, omdat de Windows-client zijn eigen
/// WPF-QrCodeHelper (die een BitmapSource teruggeeft) behoudt en beide anders
/// dubbelzinnig zouden zijn in de gedeelde namespace Spotnet.Remote.
/// </summary>
public static class RemoteQrCode
{
    /// <summary>
    /// Het QR-beeld als PNG. Null bij een lege inhoud, zoals het origineel.
    /// </summary>
    public static byte[] GeneratePng(string content, int pixelsPerModule = 10)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(pixelsPerModule);
    }
}
