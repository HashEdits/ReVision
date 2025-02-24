namespace ReVision.EyeDevice.Platform.Client;

public class CAuthorizeResponse : BasePacket
{
    public CAuthorizeResponse(byte[] challengeCode, byte[] unknownData) : base(1911)
    {
        EncodeUInt32(0x3E9);
        EncodeUInt32(0);
        EncodeBlob(challengeCode);
        EncodeBlob(unknownData);
    }
}