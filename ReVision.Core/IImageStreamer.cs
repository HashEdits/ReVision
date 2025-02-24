namespace ReVision.Core;

public interface IImageStreamer
{
    public void UpdateImageData(Eye targetEye, RGBAImage rgbaImage);
}