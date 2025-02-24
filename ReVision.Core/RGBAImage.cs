namespace ReVision.Core;

public struct RGBAImage
{
    public int Width;
    public int Height;
    public Memory<byte> Pixels;

    public YUVImage AsYUV()
    {
        Memory<double> yuvPixels = new double[Width * Height * 3];
        Span<double> yuvSpan = yuvPixels.Span;
        Span<byte> pixelSpan = Pixels.Span;

        int yuvIndex = 0;
        // This discards the alpha channel
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            byte r = pixelSpan[i];
            byte g = pixelSpan[i + 1];
            byte b = pixelSpan[i + 2];
            
            double y = r * .299000 + g * .587000 + b * .114000;
            double u = r * -.168736 + g * -.331264 + b * .500000 + 128;
            double v = r * .500000 + g * -.418688 + b * -.081312 + 128;
            
            yuvSpan[yuvIndex++] = y;
            yuvSpan[yuvIndex++] = u;
            yuvSpan[yuvIndex++] = v;
        }

        return new YUVImage()
        {
            Width = Width,
            Height = Height,
            Pixels = yuvPixels,
        };
    }
}