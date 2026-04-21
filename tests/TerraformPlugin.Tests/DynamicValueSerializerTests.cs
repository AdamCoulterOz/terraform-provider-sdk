using TerraformPlugin.Types;

namespace TerraformPlugin.Tests;

public sealed class DynamicValueSerializerTests
{
    [Fact]
    public void EncodeDecodeMsgPack_RoundTripsNestedObject()
    {
        var nestedType = new TerraformObjectType(
            new Dictionary<string, TFType>(StringComparer.Ordinal)
            {
                ["enabled"] = TFType.Bool,
                ["threshold"] = TFType.Number,
            });

        var valueType = new TerraformObjectType(
            new Dictionary<string, TFType>(StringComparer.Ordinal)
            {
                ["name"] = TFType.String,
                ["count"] = TFType.Number,
                ["tags"] = new TFMapType(TFType.String),
                ["items"] = new TFListType(TFType.String),
                ["settings"] = nestedType,
            });

        var value = DynamicValue.Object(
            valueType,
            new Dictionary<string, DynamicValue>(StringComparer.Ordinal)
            {
                ["name"] = DynamicValue.String("widget"),
                ["count"] = DynamicValue.Number(TFNumber.Parse("42")),
                ["tags"] = DynamicValue.Map(
                    TFType.String,
                    new Dictionary<string, DynamicValue>(StringComparer.Ordinal)
                    {
                        ["env"] = DynamicValue.String("test"),
                        ["tier"] = DynamicValue.String("backend"),
                    }),
                ["items"] = DynamicValue.List(
                    TFType.String,
                    [
                        DynamicValue.String("one"),
                        DynamicValue.String("two"),
                    ]),
                ["settings"] = DynamicValue.Object(
                    nestedType,
                    new Dictionary<string, DynamicValue>(StringComparer.Ordinal)
                    {
                        ["enabled"] = DynamicValue.Bool(true),
                        ["threshold"] = DynamicValue.Number(TFNumber.Parse("3.5")),
                    }),
            });

        var encoded = DynamicValueSerializer.EncodeDynamicValue(value, valueType);
        var decoded = DynamicValueSerializer.DecodeDynamicValue(encoded, valueType);

        AssertEquivalent(value, decoded);
    }

    [Fact]
    public void EncodeDecodeMsgPack_PreservesUnknownValue()
    {
        var value = DynamicValue.Unknown(TFType.String);

        var encoded = DynamicValueSerializer.EncodeDynamicValue(value, TFType.String);
        var decoded = DynamicValueSerializer.DecodeDynamicValue(encoded, TFType.String);

        Assert.True(decoded.IsUnknown);
        Assert.Equal(TFType.String, decoded.Type);
    }

    [Fact]
    public void EncodeDecodeMsgPack_RoundTripsDynamicValue()
    {
        var encoded = DynamicValueSerializer.EncodeDynamicValue(DynamicValue.String("hello"), TFType.Dynamic);
        var decoded = DynamicValueSerializer.DecodeDynamicValue(encoded, TFType.Dynamic);

        Assert.True(decoded.IsKnown);
        Assert.Equal(TFType.Dynamic, decoded.Type);

        var innerValue = Assert.IsType<DynamicValue>(decoded.Value);
        Assert.Equal(TFType.String, innerValue.Type);
        Assert.Equal("hello", innerValue.AsString());
    }

    [Fact]
    public void DecodeJsonValue_ParsesCurrentSchemaJsonState()
    {
        var valueType = new TerraformObjectType(
            new Dictionary<string, TFType>(StringComparer.Ordinal)
            {
                ["name"] = TFType.String,
                ["count"] = TFType.Number,
                ["enabled"] = TFType.Bool,
            });

        var decoded = DynamicValueSerializer.DecodeJsonValue(
            """
            {"name":"widget","count":7,"enabled":true}
            """u8.ToArray(),
            valueType);

        Assert.Equal("widget", decoded.GetAttribute("name").AsString());
        Assert.Equal("7", decoded.GetAttribute("count").AsNumber().Raw);
        Assert.True(decoded.GetAttribute("enabled").AsBoolean());
    }

    private static void AssertEquivalent(DynamicValue expected, DynamicValue actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.State, actual.State);

        if (expected.IsNull || expected.IsUnknown)
        {
            return;
        }

        switch (expected.Value)
        {
            case string expectedString:
                Assert.Equal(expectedString, actual.AsString());
                return;
            case bool expectedBoolean:
                Assert.Equal(expectedBoolean, actual.AsBoolean());
                return;
            case TFNumber expectedNumber:
                Assert.Equal(expectedNumber.Raw, actual.AsNumber().Raw);
                return;
            case IReadOnlyList<DynamicValue> expectedSequence:
                {
                    var actualSequence = actual.AsSequence();
                    Assert.Equal(expectedSequence.Count, actualSequence.Count);

                    for (var index = 0; index < expectedSequence.Count; index++)
                        AssertEquivalent(expectedSequence[index], actualSequence[index]);

                    return;
                }
            case IReadOnlyDictionary<string, DynamicValue> expectedObject:
                {
                    var actualObject = actual.AsObject();
                    Assert.Equal(expectedObject.Count, actualObject.Count);

                    foreach (var pair in expectedObject)
                    {
                        Assert.True(actualObject.ContainsKey(pair.Key), $"Expected attribute '{pair.Key}' to be present.");
                        AssertEquivalent(pair.Value, actualObject[pair.Key]);
                    }

                    return;
                }
            case DynamicValue expectedInnerValue:
                {
                    var actualInnerValue = Assert.IsType<DynamicValue>(actual.Value);
                    AssertEquivalent(expectedInnerValue, actualInnerValue);
                    return;
                }
            default:
                throw new InvalidOperationException($"Unsupported Terraform test value '{expected.Value?.GetType().Name}'.");
        }
    }
}
