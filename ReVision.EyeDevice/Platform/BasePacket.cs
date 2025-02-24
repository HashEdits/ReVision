using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace ReVision.EyeDevice.Platform;

public class BasePacket
{
    /*
        Struct discovered with help from Spotlight
        https://github.com/spotlightishere

        struct ttp_message {
            u32 message_direction;
            u32 message_length_incl_header;
            u32 payload_type;
            u32 message_id;
            u32 unk04;
            u32 op_code;
            u32 unk05;
            u32 payload_length;
        };
    */

    private const uint EXTENDED_DATA_STREAM_ROW_PROLOGUE = 0xBB8;
    private const uint EXTENDED_DATA_STREAM_COLUMN_PROLOGUE = 0x20BB9;

    private const uint TREE_LIST_PROLOGUE = 0x100;

    private int _dataOffset;
    private int _fragmentOffset;
    private Memory<byte> _data;
    private ReadOnlySpan<byte> _dataSpan => _data.Span;
    
    public uint MessageDirection { get; internal set; }
    
    public uint MessageLength { get; internal set; }
    
    public uint MessageType { get; internal set; }
    
    public uint MessageId { get; internal set; }
    
    public uint PacketId { get; internal set; }
    
    public uint PayloadLength { get; internal set; }
    
    
    public BasePacket(uint packetId)
    {
        PacketId = packetId;
        _data = new Memory<byte>(new byte[4096]);
    }

    public BasePacket(Memory<byte> data)
    {
        _data = data;
        
        MessageDirection = ReadUInt32LE();
        MessageLength = ReadUInt32LE();
        MessageType = ReadUInt32();
        MessageId = ReadUInt32();
        ReadUInt32();
        PacketId = ReadUInt32();
        ReadUInt32();
        PayloadLength = ReadUInt32();
        
        if (PayloadLength != 0)
        {
            ReadByte();
            ReadByte();
        }
        
        /*_data = new Memory<byte>(new byte[PayloadLength]);
        
        Console.WriteLine(_dataOffset..(int)(_dataOffset + PayloadLength));

        if (PayloadLength > 0)
        {
            Console.WriteLine($"{PayloadLength} >= {data.Length}");
            data[_dataOffset..].CopyTo(_data);
        }
        
        ResetPosition();*/
    }

    private void Final(uint outgoingMessageId)
    {
        MessageLength = (uint)(_dataOffset + 26);
        PayloadLength = (uint)(_dataOffset + 2);
        MessageType = 81;
        MessageId = outgoingMessageId;
    }

    public int AsOutgoingMessage(uint outgoingMessageId, ref Memory<byte> sendBuffer)
    {
        Final(outgoingMessageId);
        
        int writePosition = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(sendBuffer.Span[writePosition..], MessageDirection);
        writePosition += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(sendBuffer.Span[writePosition..], MessageLength);
        writePosition += 4;
        BinaryPrimitives.WriteUInt32BigEndian(sendBuffer.Span[writePosition..], MessageType);
        writePosition += 4;
        BinaryPrimitives.WriteUInt32BigEndian(sendBuffer.Span[writePosition..], MessageId);
        writePosition += 4;
        BinaryPrimitives.WriteUInt32BigEndian(sendBuffer.Span[writePosition..], 0);
        writePosition += 4;
        BinaryPrimitives.WriteUInt32BigEndian(sendBuffer.Span[writePosition..], PacketId);
        writePosition += 4;
        BinaryPrimitives.WriteUInt32BigEndian(sendBuffer.Span[writePosition..], 0);
        writePosition += 4;
        BinaryPrimitives.WriteUInt32BigEndian(sendBuffer.Span[writePosition..], PayloadLength);
        writePosition += 4;
        writePosition++; // 1 Byte
        writePosition++; // 1 Byte
        
        _data[.._dataOffset].CopyTo(sendBuffer[writePosition..]);
        return _dataOffset + 34;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetPosition()
    {
        _dataOffset = 0;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureAvailable(int length)
    {
        if (_dataOffset + length > _data.Length)
            throw new IndexOutOfRangeException();
    }

    private void GrowIfNeeded(int length)
    {
        int neededSize = _dataOffset + length;
        
        if (neededSize <= _data.Length)
            return;

        int targetSize = _data.Length * 2;

        while (neededSize > targetSize)
            targetSize *= 2;
        
        Memory<byte> newData = new Memory<byte>(new byte[targetSize]);
        _data.CopyTo(newData);
        _data = newData;
    }

    public void ReinitializeFragment()
    {
        Memory<byte> newData = new Memory<byte>(new byte[PayloadLength]);
        _data.CopyTo(newData);
    }
    
    public void AppendDataFragment(Span<byte> data)
    {
        GrowIfNeeded(data.Length);
        data.CopyTo(_data.Span[_fragmentOffset..]);
        _fragmentOffset += data.Length;
    }
    
    #region Primitive Read
    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        _dataOffset += sizeof(byte);
        
        return _dataSpan[_dataOffset - sizeof(byte)];
    }

    public uint ReadUInt32LE()
    {
        EnsureAvailable(sizeof(uint));
        _dataOffset += sizeof(uint);
        
        return BinaryPrimitives.ReadUInt32LittleEndian(_dataSpan[(_dataOffset - sizeof(uint))..]);
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(sizeof(uint));
        _dataOffset += sizeof(uint);
        
        return BinaryPrimitives.ReadUInt32BigEndian(_dataSpan[(_dataOffset - sizeof(uint))..]);
    }

    public int ReadInt32()
    {
        EnsureAvailable(sizeof(int));
        _dataOffset += sizeof(int);
        
        return BinaryPrimitives.ReadInt32BigEndian(_dataSpan[(_dataOffset - sizeof(int))..]);
    }

    public long ReadInt64()
    {
        EnsureAvailable(sizeof(long));
        _dataOffset += sizeof(long);
        
        return BinaryPrimitives.ReadInt64BigEndian(_dataSpan[(_dataOffset - sizeof(long))..]);
    }

    public float ReadSingle()
    {
        EnsureAvailable(sizeof(int));
        _dataOffset += sizeof(int);
        
        return BinaryPrimitives.ReadInt32BigEndian(_dataSpan[(_dataOffset - sizeof(int))..]) * 0.000015258789f;
    }
    #endregion
    
    #region Primitive Write
    public void WriteByte(byte value)
    {
        GrowIfNeeded(sizeof(byte));
        _data.Span[_dataOffset++] = value;
    }

    public void WriteUInt32LE(uint value)
    {
        GrowIfNeeded(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(_data.Span[_dataOffset..], value);
        _dataOffset += sizeof(uint);
    }

    public void WriteUInt32(uint value)
    {
        GrowIfNeeded(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(_data.Span[_dataOffset..], value);
        _dataOffset += sizeof(uint);
    }
    #endregion
    
    #region Decode
    private void CheckLengthAndType(byte type, uint length)
    {
        if (ReadByte() != type)
            throw new TypeMismatchException();

        if (ReadUInt32() != length)
            throw new InvalidLengthException();
    }

    public uint DecodeUInt32()
    {
        CheckLengthAndType(0x02, 0x04);

        return ReadUInt32();
    }

    public long DecodeInt64()
    {
        CheckLengthAndType(0x06, 0x08);
        
        return ReadInt64();
    }

    public float DecodeSingle()
    {
        CheckLengthAndType(0x03, 0x04);
        
        return ReadSingle();
    }

    public uint DecodePrologue()
    {
        CheckLengthAndType(0x05, 0x04);

        return ReadUInt32();
    }

    public string DecodeString()
    {
        if (ReadByte() != 0x14)
            throw new TypeMismatchException();
        
        EnsureAvailable((int)ReadUInt32());
        uint dataLength = ReadUInt32();
        _dataOffset += (int)dataLength;
        
        return Encoding.ASCII.GetString(_data[(_dataOffset - (int)dataLength).._dataOffset].Span);
    }

    public ReadOnlyMemory<byte> DecodeBlob()
    {
        if (ReadByte() != 0x15)
            throw new TypeMismatchException();
        
        EnsureAvailable((int)ReadUInt32());
        uint dataLength = ReadUInt32();
        _dataOffset += (int)dataLength;
        
        return _data[(_dataOffset - (int)dataLength).._dataOffset];
    }

    public uint DecodeExtendedDataStreamRow()
    {
        uint prologue = DecodePrologue();
        
        if ((prologue & 0xFFF) != EXTENDED_DATA_STREAM_ROW_PROLOGUE)
            throw new TypeMismatchException();

        return prologue >> 16;
    }

    public uint DecodeExtendedDataStreamColumn()
    {
        uint prologue = DecodePrologue();

        if (prologue != EXTENDED_DATA_STREAM_COLUMN_PROLOGUE)
            throw new TypeAccessException();

        return DecodeUInt32();
    }

    public (uint listCount, uint unknownCount) DecodeList()
    {
        uint prologue = DecodePrologue();
        
        if ((prologue & 0xFFF) != TREE_LIST_PROLOGUE)
            throw new TypeMismatchException();
        
        return ((prologue >> 16) - 1, DecodeUInt32());
    }
    #endregion
    
    #region Encode
    public void EncodeUInt32(uint value)
    {
        WriteByte(0x02);
        WriteUInt32(0x04);
        WriteUInt32(value);
    }
    
    public void EncodeUInt32Vector(uint[] vector)
    {
       WriteByte(0x17);
       int vectorLength = vector.Length;
       WriteUInt32((uint)((vectorLength * 4) + 4));
       WriteUInt32((uint)vectorLength);
       
       for (int i = 0; i < vectorLength; i++)
           WriteUInt32(vector[i]);
    }

    public void EncodeBlob(byte[] blob)
    {
        WriteByte(0x15);
        uint blobLength = (uint)blob.Length;
        WriteUInt32(blobLength + 4);
        WriteUInt32(blobLength);
        
        GrowIfNeeded(blob.Length);
        blob.CopyTo(_data[_dataOffset..]);
        
        _dataOffset += (int)blobLength;
    }
    #endregion
}