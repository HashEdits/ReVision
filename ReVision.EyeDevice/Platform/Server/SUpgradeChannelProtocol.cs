namespace ReVision.EyeDevice.Platform.Server;

public class SUpgradeChannelProtocol : BasePacket
{
    public uint NegotiatedVersion { get; init; }

    public SUpgradeChannelProtocol(Memory<byte> data) : base(data)
    {
        NegotiatedVersion = DecodeUInt32();
    }
}