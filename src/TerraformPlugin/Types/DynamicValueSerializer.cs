using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MessagePack;
using Tfplugin6;
using ProtocolDynamicValue = Tfplugin6.DynamicValue;

namespace TerraformPlugin.Types;

internal static class DynamicValueSerializer
{
    public static DynamicValue DecodeDynamicValue(ProtocolDynamicValue dynamicValue, TFType type)
    {
        if (dynamicValue.Msgpack.Length > 0)
        {
            return DecodeMsgPack(dynamicValue.Msgpack.Memory, type);
        }

        if (dynamicValue.Json.Length > 0)
        {
            return DecodeJson(dynamicValue.Json.Span, type);
        }

        throw new InvalidOperationException("DynamicValue did not contain msgpack or JSON data.");
    }

    public static ProtocolDynamicValue EncodeDynamicValue(DynamicValue value, TFType type) =>
        new()
        {
            Msgpack = Google.Protobuf.ByteString.CopyFrom(EncodeMsgPack(value, type)),
        };

    public static DynamicValue DecodeMsgPack(ReadOnlyMemory<byte> bytes, TFType type)
    {
        var reader = new MessagePackReader(bytes);
        var value = ReadMsgPackValue(ref reader, type);

        if (reader.End is false)
        {
            throw new InvalidOperationException("Terraform msgpack payload contained trailing bytes.");
        }

        return value;
    }

    public static DynamicValue DecodeJsonValue(ReadOnlySpan<byte> bytes, TFType type) =>
        DecodeJson(bytes, type);

    public static byte[] EncodeMsgPack(DynamicValue value, TFType type)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        WriteMsgPackValue(ref writer, value, type);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static DynamicValue ReadMsgPackValue(ref MessagePackReader reader, TFType type)
    {
        if (reader.NextMessagePackType == MessagePackType.Extension)
        {
            reader.Skip();
            return DynamicValue.Unknown(type);
        }

        if (type.Equals(TFType.Dynamic))
        {
            return ReadDynamicMsgPackValue(ref reader);
        }

        if (reader.TryReadNil())
        {
            return DynamicValue.Null(type);
        }

        return type switch
        {
            TerraformPrimitiveType primitive when primitive.Kind == "string" => DynamicValue.String(reader.ReadString() ?? string.Empty),
            TerraformPrimitiveType primitive when primitive.Kind == "number" => DynamicValue.Number(ReadNumber(ref reader)),
            TerraformPrimitiveType primitive when primitive.Kind == "bool" => DynamicValue.Bool(reader.ReadBoolean()),
            TFListType list => ReadListValue(ref reader, list.ElementType, false),
            TFSetType set => ReadListValue(ref reader, set.ElementType, true),
            TFMapType map => ReadMapValue(ref reader, map.ElementType),
            TerraformTupleType tuple => ReadTupleValue(ref reader, tuple.ElementTypes),
            TerraformObjectType obj => ReadObjectValue(ref reader, obj),
            _ => throw new InvalidOperationException($"Unsupported Terraform type '{type}'."),
        };
    }

    private static DynamicValue ReadDynamicMsgPackValue(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();

        if (arrayLength == -1)
        {
            return DynamicValue.Null(TFType.Dynamic);
        }

        if (arrayLength != 2)
        {
            throw new InvalidOperationException($"Dynamic Terraform values must be encoded as a 2-element array. Saw {arrayLength}.");
        }

        var typeBytes = reader.ReadBytes() ?? throw new InvalidOperationException("Dynamic Terraform values must include encoded type bytes.");
        var actualType = TFType.ParseTypeJson(typeBytes.ToArray());
        var actualValue = ReadMsgPackValue(ref reader, actualType);
        return DynamicValue.Known(TFType.Dynamic, actualValue);
    }

    private static DynamicValue ReadListValue(ref MessagePackReader reader, TFType elementType, bool isSet)
    {
        var length = reader.ReadArrayHeader();
        var values = new DynamicValue[length];

        for (var index = 0; index < length; index++)
        {
            values[index] = ReadMsgPackValue(ref reader, elementType);
        }

        return isSet
            ? DynamicValue.Set(elementType, values)
            : DynamicValue.List(elementType, values);
    }

    private static DynamicValue ReadTupleValue(ref MessagePackReader reader, IReadOnlyList<TFType> elementTypes)
    {
        var length = reader.ReadArrayHeader();

        if (length != elementTypes.Count)
        {
            throw new InvalidOperationException($"Terraform tuple expected {elementTypes.Count} elements, saw {length}.");
        }

        var values = new DynamicValue[length];

        for (var index = 0; index < length; index++)
        {
            values[index] = ReadMsgPackValue(ref reader, elementTypes[index]);
        }

        return DynamicValue.Tuple(elementTypes, values);
    }

    private static DynamicValue ReadMapValue(ref MessagePackReader reader, TFType elementType)
    {
        var length = reader.ReadMapHeader();
        var values = new Dictionary<string, DynamicValue>(length, StringComparer.Ordinal);

        for (var index = 0; index < length; index++)
        {
            var key = reader.ReadString() ?? string.Empty;
            values[key] = ReadMsgPackValue(ref reader, elementType);
        }

        return DynamicValue.Map(elementType, values);
    }

    private static DynamicValue ReadObjectValue(ref MessagePackReader reader, TerraformObjectType objectType)
    {
        var length = reader.ReadMapHeader();

        if (length != objectType.AttributeTypes.Count)
        {
            throw new InvalidOperationException($"Terraform object expected {objectType.AttributeTypes.Count} attributes, saw {length}.");
        }

        var values = new Dictionary<string, DynamicValue>(length, StringComparer.Ordinal);

        for (var index = 0; index < length; index++)
        {
            var key = reader.ReadString() ?? string.Empty;

            if (!objectType.AttributeTypes.TryGetValue(key, out var attributeType))
            {
                throw new InvalidOperationException($"Unknown Terraform object attribute '{key}'.");
            }

            values[key] = ReadMsgPackValue(ref reader, attributeType);
        }

        return DynamicValue.Object(objectType, values);
    }

    private static TFNumber ReadNumber(ref MessagePackReader reader)
    {
        return reader.NextMessagePackType switch
        {
            MessagePackType.Integer => ReadIntegerNumber(ref reader),
            MessagePackType.Float => TFNumber.FromDouble(reader.ReadDouble()),
            MessagePackType.String => TFNumber.Parse(reader.ReadString() ?? "0"),
            _ => throw new InvalidOperationException("Terraform number value was not encoded as an integer, float, or string."),
        };
    }

    private static TFNumber ReadIntegerNumber(ref MessagePackReader reader)
    {
        var peekReader = reader.CreatePeekReader();

        try
        {
            var signed = peekReader.ReadInt64();
            return TFNumber.FromInt64(reader.ReadInt64());
        }
        catch (OverflowException)
        {
            return TFNumber.FromUInt64(reader.ReadUInt64());
        }
    }

    private static void WriteMsgPackValue(ref MessagePackWriter writer, DynamicValue value, TFType type)
    {
        if (type.Equals(TFType.Dynamic) && value.Type.Equals(TFType.Dynamic) is false)
        {
            writer.WriteArrayHeader(2);
            writer.Write(Encoding.UTF8.GetBytes(value.Type.ToTypeJson()));
            WriteMsgPackValue(ref writer, value, value.Type);
            return;
        }

        if (value.IsUnknown)
        {
            writer.WriteExtensionFormatHeader(new ExtensionHeader(0, 1));
            writer.Write((byte)0);
            return;
        }

        if (value.IsNull)
        {
            writer.WriteNil();
            return;
        }

        switch (type)
        {
            case TerraformPrimitiveType primitive when primitive.Kind == "string":
                writer.Write(value.AsString());
                return;
            case TerraformPrimitiveType primitive when primitive.Kind == "number":
                WriteNumber(ref writer, value.AsNumber());
                return;
            case TerraformPrimitiveType primitive when primitive.Kind == "bool":
                writer.Write(value.AsBoolean());
                return;
            case TFListType list:
                WriteSequence(ref writer, value.AsSequence(), list.ElementType);
                return;
            case TFSetType set:
                WriteSequence(ref writer, value.AsSequence(), set.ElementType);
                return;
            case TFMapType map:
                WriteMap(ref writer, value.AsObject(), map.ElementType);
                return;
            case TerraformTupleType tuple:
                WriteTuple(ref writer, value.AsSequence(), tuple.ElementTypes);
                return;
            case TerraformObjectType obj:
                WriteObject(ref writer, value.AsObject(), obj);
                return;
            default:
                throw new InvalidOperationException($"Unsupported Terraform type '{type}'.");
        }
    }

    private static void WriteSequence(ref MessagePackWriter writer, IReadOnlyList<DynamicValue> values, TFType elementType)
    {
        writer.WriteArrayHeader(values.Count);

        foreach (var item in values)
        {
            WriteMsgPackValue(ref writer, item, elementType);
        }
    }

    private static void WriteTuple(ref MessagePackWriter writer, IReadOnlyList<DynamicValue> values, IReadOnlyList<TFType> elementTypes)
    {
        writer.WriteArrayHeader(values.Count);

        for (var index = 0; index < values.Count; index++)
        {
            WriteMsgPackValue(ref writer, values[index], elementTypes[index]);
        }
    }

    private static void WriteMap(ref MessagePackWriter writer, IReadOnlyDictionary<string, DynamicValue> values, TFType elementType)
    {
        writer.WriteMapHeader(values.Count);

        foreach (var pair in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Write(pair.Key);
            WriteMsgPackValue(ref writer, pair.Value, elementType);
        }
    }

    private static void WriteObject(ref MessagePackWriter writer, IReadOnlyDictionary<string, DynamicValue> values, TerraformObjectType objectType)
    {
        writer.WriteMapHeader(objectType.AttributeTypes.Count);

        foreach (var attribute in objectType.AttributeTypes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!values.TryGetValue(attribute.Key, out var value))
            {
                throw new InvalidOperationException($"Terraform object value did not contain required attribute '{attribute.Key}'.");
            }

            writer.Write(attribute.Key);
            WriteMsgPackValue(ref writer, value, attribute.Value);
        }
    }

    private static void WriteNumber(ref MessagePackWriter writer, TFNumber number)
    {
        if (number.TryGetInt64(out var signed))
        {
            writer.Write(signed);
            return;
        }

        if (number.TryGetUInt64(out var unsigned))
        {
            writer.Write(unsigned);
            return;
        }

        if (double.TryParse(number.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            writer.Write(doubleValue);
            return;
        }

        writer.Write(number.Raw);
    }

    private static DynamicValue DecodeJson(ReadOnlySpan<byte> bytes, TFType type)
    {
        using var document = JsonDocument.Parse(bytes.ToArray());
        return DecodeJson(document.RootElement, type);
    }

    private static DynamicValue DecodeJson(JsonElement element, TFType type)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return DynamicValue.Null(type);
        }

        if (type.Equals(TFType.Dynamic))
        {
            throw new InvalidOperationException("Dynamic Terraform values are not supported in JSON mode.");
        }

        return type switch
        {
            TerraformPrimitiveType primitive when primitive.Kind == "string" => DynamicValue.String(element.GetString() ?? string.Empty),
            TerraformPrimitiveType primitive when primitive.Kind == "number" => DynamicValue.Number(TFNumber.Parse(element.GetRawText())),
            TerraformPrimitiveType primitive when primitive.Kind == "bool" => DynamicValue.Bool(element.GetBoolean()),
            TFListType list => DynamicValue.List(list.ElementType, element.EnumerateArray().Select(item => DecodeJson(item, list.ElementType)).ToArray()),
            TFSetType set => DynamicValue.Set(set.ElementType, element.EnumerateArray().Select(item => DecodeJson(item, set.ElementType)).ToArray()),
            TFMapType map => DynamicValue.Map(
                map.ElementType,
                element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    property => DecodeJson(property.Value, map.ElementType),
                    StringComparer.Ordinal)),
            TerraformTupleType tuple => DynamicValue.Tuple(
                tuple.ElementTypes,
                element.EnumerateArray().Select((item, index) => DecodeJson(item, tuple.ElementTypes[index])).ToArray()),
            TerraformObjectType obj => DynamicValue.Object(
                obj,
                element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    property =>
                    {
                        if (!obj.AttributeTypes.TryGetValue(property.Name, out var attributeType))
                        {
                            throw new InvalidOperationException($"Unknown Terraform object attribute '{property.Name}'.");
                        }

                        return DecodeJson(property.Value, attributeType);
                    },
                    StringComparer.Ordinal)),
            _ => throw new InvalidOperationException($"Unsupported Terraform type '{type}'."),
        };
    }
}
