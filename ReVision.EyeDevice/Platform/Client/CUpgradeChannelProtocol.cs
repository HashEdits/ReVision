namespace ReVision.EyeDevice.Platform.Client;

public class CUpgradeChannelProtocol : BasePacket
{
    public CUpgradeChannelProtocol(uint[] supportedVersions) : base(1000)
    {
        EncodeUInt32Vector(supportedVersions);
    }
}