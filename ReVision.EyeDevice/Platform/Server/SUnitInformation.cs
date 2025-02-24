namespace ReVision.EyeDevice.Platform.Server;

public class SUnitInformation : BasePacket
{
    public string SerialNumber { get; init; }
    
    public string DeviceModel { get; init; }
    
    public string DeviceLine { get; init; }
    
    public string FirmwareVersion { get; init; }
    
    public SUnitInformation(Memory<byte> data) : base(data)
    {
        SerialNumber = DecodeString();
        DeviceModel = DecodeString();
        DeviceLine = DecodeString();
        FirmwareVersion = DecodeString();
    }
}