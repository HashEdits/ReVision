namespace ReVision.EyeDevice.Platform.Client;

public class CAuthorizeChallenge : BasePacket
{
    public CAuthorizeChallenge() : base(1900)
    {
        EncodeUInt32(0x3E9);
        EncodeUInt32Vector([0]);
    }
}