using QRCoder;

namespace TAPortal.Integrations;

/// <summary>Renders a VietQR/EMV payload to an inline SVG (no external service, no image file).</summary>
public static class QrImage
{
    public static string Svg(string payload, int pixelsPerModule = 6)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(pixelsPerModule, "#0F172A", "#FFFFFF", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }
}
