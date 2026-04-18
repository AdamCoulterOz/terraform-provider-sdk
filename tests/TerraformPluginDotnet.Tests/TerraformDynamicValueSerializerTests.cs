using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Tests;

public sealed class TerraformDynamicValueSerializerTests
{
    [Fact]
    public void EncodeDecodeMsgPack_RoundTripsNestedObject()
    {
        var nestedType = new TerraformObjectType(
            new Dictionary<string, TerraformType>(StringComparer.Ordinal)
            {
                ["enabled"] = TerraformType.Bool,
                ["threshold"] = TerraformType.Number,
            });

        var valueType = new TerraformObjectType(
            new Dictionary<string, TerraformType>(StringComparer.Ordinal)
            {
                ["name"] = TerraformType.String,
                ["count"] = TerraformType.Number,
                ["tags"] = new TerraformMapType(TerraformType.String),
                ["items"] = new TerraformListType(TerraformType.String),
                ["settings"] = nestedType,
            });

        var value = TerraformDynamicValue.Object(
            valueType,
            new Dictionary<string, TerraformDynamicValue>(StringComparer.Ordinal)
            {
                ["name"] = TerraformDynamicValue.String("widget"),
                ["count"] = TerraformDynamicValue.Number(TerraformNumber.Parse("42")),
                ["tags"] = TerraformDynamicValue.Map(
                    TerraformType.String,
                    new Dictionary<string, TerraformDynamicValue>(StringComparer.Ordinal)
                    {
                        ["env"] = TerraformDynamicValue.String("test"),
                        ["tier"] = TerraformDynamicValue.String("backend"),
                    }),
                ["items"] = TerraformDynamicValue.List(
                    TerraformType.String,
                    [
                        TerraformDynamicValue.String("one"),
                        TerraformDynamicValue.String("two"),
                    ]),
                ["settings"] = TerraformDynamicValue.Object(
                    nestedType,
                    new Dictionary<string, TerraformDynamicValue>(StringComparer.Ordinal)
                    {
                        ["enabled"] = TerraformDynamicValue.Bool(true),
                        ["threshold"] = TerraformDynamicValue.Number(TerraformNumber.Parse("3.5")),
                    }),
            });

        var encoded = TerraformDynamicValueSerializer.EncodeDynamicValue(value, valueType);
        var decoded = TerraformDynamicValueSerializer.DecodeDynamicValue(encoded, valueType);

        AssertEquivalent(value, decoded);
    }

    [Fact]
    public void EncodeDecodeMsgPack_PreservesUnknownValue()
    {
        var value = TerraformDynamicValue.Unknown(TerraformType.String);

        var encoded = TerraformDynamicValueSerializer.EncodeDynamicValue(value, TerraformType.String);
        var decoded = TerraformDynamicValueSerializer.DecodeDynamicValue(encoded, TerraformType.String);

        Assert.True(decoded.IsUnknown);
        Assert.Equal(TerraformType.String, decoded.Type);
    }

    [Fact]
    public void EncodeDecodeMsgPack_RoundTripsDynamicValue()
    {
        var encoded = TerraformDynamicValueSerializer.EncodeDynamicValue(TerraformDynamicValue.String("hello"), TerraformType.Dynamic);
        var decoded = TerraformDynamicValueSerializer.DecodeDynamicValue(encoded, TerraformType.Dynamic);

        Assert.True(decoded.IsKnown);
        Assert.Equal(TerraformType.Dynamic, decoded.Type);

        var innerValue = Assert.IsType<TerraformDynamicValue>(decoded.Value);
        Assert.Equal(TerraformType.String, innerValue.Type);
        Assert.Equal("hello", innerValue.AsString());
    }

    [Fact]
    public void DecodeJsonValue_ParsesCurrentSchemaJsonState()
    {
        var valueType = new TerraformObjectType(
            new Dictionary<string, TerraformType>(StringComparer.Ordinal)
            {
                ["name"] = TerraformType.String,
                ["count"] = TerraformType.Number,
                ["enabled"] = TerraformType.Bool,
            });

        var decoded = TerraformDynamicValueSerializer.DecodeJsonValue(
            """
            {"name":"widget","count":7,"enabled":true}
            """u8.ToArray(),
            valueType);

        Assert.Equal("widget", decoded.GetAttribute("name").AsString());
        Assert.Equal("7", decoded.GetAttribute("count").AsNumber().Raw);
        Assert.True(decoded.GetAttribute("enabled").AsBoolean());
    }

    private static void AssertEquivalent(TerraformDynamicValue expected, TerraformDynamicValue actual)
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
            case TerraformNumber expectedNumber:
                Assert.Equal(expectedNumber.Raw, actual.AsNumber().Raw);
                return;
            case IReadOnlyList<TerraformDynamicValue> expectedSequence:
            {
                var actualSequence = actual.AsSequence();
                Assert.Equal(expectedSequence.Count, actualSequence.Count);

                for (var index = 0; index < expectedSequence.Count; index++)
                {
                    AssertEquivalent(expectedSequence[index], actualSequence[index]);
                }

                return;
            }
            case IReadOnlyDictionary<string, TerraformDynamicValue> expectedObject:
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
            case TerraformDynamicValue expectedInnerValue:
            {
                var actualInnerValue = Assert.IsType<TerraformDynamicValue>(actual.Value);
                AssertEquivalent(expectedInnerValue, actualInnerValue);
                return;
            }
            default:
                throw new InvalidOperationException($"Unsupported Terraform test value '{expected.Value?.GetType().Name}'.");
        }
    }
}
