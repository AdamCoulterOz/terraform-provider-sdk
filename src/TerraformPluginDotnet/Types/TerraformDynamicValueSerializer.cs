using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MessagePack;
using Tfplugin6;

namespace TerraformPluginDotnet.Types;

internal static class TerraformDynamicValueSerializer
{
    public static TerraformDynamicValue DecodeDynamicValue(DynamicValue dynamicValue, TerraformType type)
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

    public static DynamicValue EncodeDynamicValue(TerraformDynamicValue value, TerraformType type) =>
        new()
        {
            Msgpack = Google.Protobuf.ByteString.CopyFrom(EncodeMsgPack(value, type)),
        };

    public static TerraformDynamicValue DecodeMsgPack(ReadOnlyMemory<byte> bytes, TerraformType type)
    {
        var reader = new MessagePackReader(bytes);
        var value = ReadMsgPackValue(ref reader, type);

        if (reader.End is false)
        {
            throw new InvalidOperationException("Terraform msgpack payload contained trailing bytes.");
        }

        return value;
    }

    public static TerraformDynamicValue DecodeJsonValue(ReadOnlySpan<byte> bytes, TerraformType type) =>
        DecodeJson(bytes, type);

    public static byte[] EncodeMsgPack(TerraformDynamicValue value, TerraformType type)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        WriteMsgPackValue(ref writer, value, type);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static TerraformDynamicValue ReadMsgPackValue(ref MessagePackReader reader, TerraformType type)
    {
        if (reader.NextMessagePackType == MessagePackType.Extension)
        {
            reader.Skip();
            return TerraformDynamicValue.Unknown(type);
        }

        if (type.Equals(TerraformType.Dynamic))
        {
            return ReadDynamicMsgPackValue(ref reader);
        }

        if (reader.TryReadNil())
        {
            return TerraformDynamicValue.Null(type);
        }

        return type switch
        {
            TerraformPrimitiveType primitive when primitive.Kind == "string" => TerraformDynamicValue.String(reader.ReadString() ?? string.Empty),
            TerraformPrimitiveType primitive when primitive.Kind == "number" => TerraformDynamicValue.Number(ReadNumber(ref reader)),
            TerraformPrimitiveType primitive when primitive.Kind == "bool" => TerraformDynamicValue.Bool(reader.ReadBoolean()),
            TerraformListType list => ReadListValue(ref reader, list.ElementType, false),
            TerraformSetType set => ReadListValue(ref reader, set.ElementType, true),
            TerraformMapType map => ReadMapValue(ref reader, map.ElementType),
            TerraformTupleType tuple => ReadTupleValue(ref reader, tuple.ElementTypes),
            TerraformObjectType obj => ReadObjectValue(ref reader, obj),
            _ => throw new InvalidOperationException($"Unsupported Terraform type '{type}'."),
        };
    }

    private static TerraformDynamicValue ReadDynamicMsgPackValue(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();

        if (arrayLength == -1)
        {
            return TerraformDynamicValue.Null(TerraformType.Dynamic);
        }

        if (arrayLength != 2)
        {
            throw new InvalidOperationException($"Dynamic Terraform values must be encoded as a 2-element array. Saw {arrayLength}.");
        }

        var typeBytes = reader.ReadBytes() ?? throw new InvalidOperationException("Dynamic Terraform values must include encoded type bytes.");
        var actualType = TerraformType.ParseTypeJson(typeBytes.ToArray());
        var actualValue = ReadMsgPackValue(ref reader, actualType);
        return TerraformDynamicValue.Known(TerraformType.Dynamic, actualValue);
    }

    private static TerraformDynamicValue ReadListValue(ref MessagePackReader reader, TerraformType elementType, bool isSet)
    {
        var length = reader.ReadArrayHeader();
        var values = new TerraformDynamicValue[length];

        for (var index = 0; index < length; index++)
        {
            values[index] = ReadMsgPackValue(ref reader, elementType);
        }

        return isSet
            ? TerraformDynamicValue.Set(elementType, values)
            : TerraformDynamicValue.List(elementType, values);
    }

    private static TerraformDynamicValue ReadTupleValue(ref MessagePackReader reader, IReadOnlyList<TerraformType> elementTypes)
    {
        var length = reader.ReadArrayHeader();

        if (length != elementTypes.Count)
        {
            throw new InvalidOperationException($"Terraform tuple expected {elementTypes.Count} elements, saw {length}.");
        }

        var values = new TerraformDynamicValue[length];

        for (var index = 0; index < length; index++)
        {
            values[index] = ReadMsgPackValue(ref reader, elementTypes[index]);
        }

        return TerraformDynamicValue.Tuple(elementTypes, values);
    }

    private static TerraformDynamicValue ReadMapValue(ref MessagePackReader reader, TerraformType elementType)
    {
        var length = reader.ReadMapHeader();
        var values = new Dictionary<string, TerraformDynamicValue>(length, StringComparer.Ordinal);

        for (var index = 0; index < length; index++)
        {
            var key = reader.ReadString() ?? string.Empty;
            values[key] = ReadMsgPackValue(ref reader, elementType);
        }

        return TerraformDynamicValue.Map(elementType, values);
    }

    private static TerraformDynamicValue ReadObjectValue(ref MessagePackReader reader, TerraformObjectType objectType)
    {
        var length = reader.ReadMapHeader();

        if (length != objectType.AttributeTypes.Count)
        {
            throw new InvalidOperationException($"Terraform object expected {objectType.AttributeTypes.Count} attributes, saw {length}.");
        }

        var values = new Dictionary<string, TerraformDynamicValue>(length, StringComparer.Ordinal);

        for (var index = 0; index < length; index++)
        {
            var key = reader.ReadString() ?? string.Empty;

            if (!objectType.AttributeTypes.TryGetValue(key, out var attributeType))
            {
                throw new InvalidOperationException($"Unknown Terraform object attribute '{key}'.");
            }

            values[key] = ReadMsgPackValue(ref reader, attributeType);
        }

        return TerraformDynamicValue.Object(objectType, values);
    }

    private static TerraformNumber ReadNumber(ref MessagePackReader reader)
    {
        return reader.NextMessagePackType switch
        {
            MessagePackType.Integer => ReadIntegerNumber(ref reader),
            MessagePackType.Float => TerraformNumber.FromDouble(reader.ReadDouble()),
            MessagePackType.String => TerraformNumber.Parse(reader.ReadString() ?? "0"),
            _ => throw new InvalidOperationException("Terraform number value was not encoded as an integer, float, or string."),
        };
    }

    private static TerraformNumber ReadIntegerNumber(ref MessagePackReader reader)
    {
        var peekReader = reader.CreatePeekReader();

        try
        {
            var signed = peekReader.ReadInt64();
            return TerraformNumber.FromInt64(reader.ReadInt64());
        }
        catch (OverflowException)
        {
            return TerraformNumber.FromUInt64(reader.ReadUInt64());
        }
    }

    private static void WriteMsgPackValue(ref MessagePackWriter writer, TerraformDynamicValue value, TerraformType type)
    {
        if (type.Equals(TerraformType.Dynamic) && value.Type.Equals(TerraformType.Dynamic) is false)
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
            case TerraformListType list:
                WriteSequence(ref writer, value.AsSequence(), list.ElementType);
                return;
            case TerraformSetType set:
                WriteSequence(ref writer, value.AsSequence(), set.ElementType);
                return;
            case TerraformMapType map:
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

    private static void WriteSequence(ref MessagePackWriter writer, IReadOnlyList<TerraformDynamicValue> values, TerraformType elementType)
    {
        writer.WriteArrayHeader(values.Count);

        foreach (var item in values)
        {
            WriteMsgPackValue(ref writer, item, elementType);
        }
    }

    private static void WriteTuple(ref MessagePackWriter writer, IReadOnlyList<TerraformDynamicValue> values, IReadOnlyList<TerraformType> elementTypes)
    {
        writer.WriteArrayHeader(values.Count);

        for (var index = 0; index < values.Count; index++)
        {
            WriteMsgPackValue(ref writer, values[index], elementTypes[index]);
        }
    }

    private static void WriteMap(ref MessagePackWriter writer, IReadOnlyDictionary<string, TerraformDynamicValue> values, TerraformType elementType)
    {
        writer.WriteMapHeader(values.Count);

        foreach (var pair in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Write(pair.Key);
            WriteMsgPackValue(ref writer, pair.Value, elementType);
        }
    }

    private static void WriteObject(ref MessagePackWriter writer, IReadOnlyDictionary<string, TerraformDynamicValue> values, TerraformObjectType objectType)
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

    private static void WriteNumber(ref MessagePackWriter writer, TerraformNumber number)
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

    private static TerraformDynamicValue DecodeJson(ReadOnlySpan<byte> bytes, TerraformType type)
    {
        using var document = JsonDocument.Parse(bytes.ToArray());
        return DecodeJson(document.RootElement, type);
    }

    private static TerraformDynamicValue DecodeJson(JsonElement element, TerraformType type)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return TerraformDynamicValue.Null(type);
        }

        if (type.Equals(TerraformType.Dynamic))
        {
            throw new InvalidOperationException("Dynamic Terraform values are not supported in JSON mode.");
        }

        return type switch
        {
            TerraformPrimitiveType primitive when primitive.Kind == "string" => TerraformDynamicValue.String(element.GetString() ?? string.Empty),
            TerraformPrimitiveType primitive when primitive.Kind == "number" => TerraformDynamicValue.Number(TerraformNumber.Parse(element.GetRawText())),
            TerraformPrimitiveType primitive when primitive.Kind == "bool" => TerraformDynamicValue.Bool(element.GetBoolean()),
            TerraformListType list => TerraformDynamicValue.List(list.ElementType, element.EnumerateArray().Select(item => DecodeJson(item, list.ElementType)).ToArray()),
            TerraformSetType set => TerraformDynamicValue.Set(set.ElementType, element.EnumerateArray().Select(item => DecodeJson(item, set.ElementType)).ToArray()),
            TerraformMapType map => TerraformDynamicValue.Map(
                map.ElementType,
                element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    property => DecodeJson(property.Value, map.ElementType),
                    StringComparer.Ordinal)),
            TerraformTupleType tuple => TerraformDynamicValue.Tuple(
                tuple.ElementTypes,
                element.EnumerateArray().Select((item, index) => DecodeJson(item, tuple.ElementTypes[index])).ToArray()),
            TerraformObjectType obj => TerraformDynamicValue.Object(
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
