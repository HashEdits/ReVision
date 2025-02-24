namespace ReVision.EyeDevice.Platform.Client;

public class CInternalCommandFeatureLock : BasePacket
{
    public CInternalCommandFeatureLock(uint featureId) : base(1915)
    {
        EncodeUInt32(featureId);
    }
}