using System.Runtime.CompilerServices;

namespace ReVision.EyeDevice.Platform.Server;

public class SImageCollection : BasePacket
{
    private const uint TREE_REGION_GROUP_CONFIG_PROLOGUE = 0x932C8;
    private const uint TREE_LED_CONFIG_PROLOGUE = 0x42EE0;

    public enum EyeRegion
    {
        Left = 0,
        Right = 1
    }
    
    public long TimestampTrackerUs { get; set; }
    
    public EyeRegion Region { get; set; }
    
    private List<ReadOnlyMemory<byte>> _imageBlobs = [];
    public IReadOnlyList<ReadOnlyMemory<byte>> ImageBlobs => _imageBlobs;
    
    public SImageCollection(Memory<byte> data) : base(data)
    {
        uint dataRows = DecodeExtendedDataStreamRow();
        uint currentRow = 0;

        if (dataRows <= 0)
            return;

        uint dataColumn = 0;
        while ((dataColumn = DecodeExtendedDataStreamColumn()) != 0)
        {
            switch (dataColumn)
            {
                case 1:
                    TimestampTrackerUs = DecodeInt64();
                    break;
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 9:
                case 10:
                case 11:
                case 12:
                case 13:
                    uint val2 = DecodeUInt32();
                    break;
                
                case 8:
                    (uint regionListCount, uint regionUnknownCount) = DecodeList();

                    for (int i = 0; i < regionListCount; i++)
                    {
                        uint regionConfig = DecodeRegionGroupConfig();
                        Region = (EyeRegion)(regionConfig & 1);
                    }
                    break;
                case 14:
                    (uint ledListCount, uint ledUnknownCount) = DecodeList();

                    for (int i = 0; i < ledListCount; i++)
                    {
                        uint ledConfig = DecodeLedConfig();
                    }
                    break;
                case 15:
                    _imageBlobs.Add(DecodeBlob());
                    break;
            }

            if (++currentRow >= dataRows)
                break;
        }
    }

    private uint DecodeRegionGroupConfig()
    {
        uint prologue = DecodePrologue();

        if (prologue != TREE_REGION_GROUP_CONFIG_PROLOGUE)
            throw new TypeMismatchException();
        
        return 
            DecodeUInt32() |
            Unsafe.BitCast<float, uint>(DecodeSingle()) |
            Unsafe.BitCast<float, uint>(DecodeSingle()) |
            DecodeUInt32() |
            DecodeUInt32() |
            DecodeUInt32() |
            DecodeUInt32() |
            DecodeUInt32() |
            DecodeUInt32();
    }

    private uint DecodeLedConfig()
    {
        uint prologue = DecodePrologue();
        
        if (prologue != TREE_LED_CONFIG_PROLOGUE)
            throw new TypeMismatchException();

        return
            DecodeUInt32() |
            DecodeUInt32() |
            DecodeUInt32() |
            Unsafe.BitCast<float, uint>(DecodeSingle());
    }
}