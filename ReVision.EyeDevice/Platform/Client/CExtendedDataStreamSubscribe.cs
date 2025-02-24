namespace ReVision.EyeDevice.Platform.Client;

public class CExtendedDataStreamSubscribe : BasePacket
{
    public CExtendedDataStreamSubscribe(uint streamId) : base(1220)
    {
        EncodeUInt32(streamId);
        EncodeUInt32Vector([]);
    }
}