namespace ReVision.EyeDevice.Platform.Server;

public class SAuthorizeChallenge : BasePacket
{
    public ReadOnlyMemory<byte> ChallengeData { get; init; }
    
    public SAuthorizeChallenge(Memory<byte> data) : base(data)
    {
        DecodeUInt32();
        DecodeUInt32();
        ChallengeData = DecodeBlob();
    }
}