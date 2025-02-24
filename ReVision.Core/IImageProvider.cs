namespace ReVision.Core;

public interface IImageProvider
{
    public delegate void UpdateImageData(Eye targetEye, RGBAImage imageSource);
    public event UpdateImageData OnUpdateImageData;
}