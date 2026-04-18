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

        var value = TerraformValue.Object(
            valueType,
            new Dictionary<string, TerraformValue>(StringComparer.Ordinal)
            {
                ["name"] = TerraformValue.String("widget"),
                ["count"] = TerraformValue.Number(TerraformNumber.Parse("42")),
                ["tags"] = TerraformValue.Map(
                    TerraformType.String,
                    new Dictionary<string, TerraformValue>(StringComparer.Ordinal)
                    {
                        ["env"] = TerraformValue.String("test"),
                        ["tier"] = TerraformValue.String("backend"),
                    }),
                ["items"] = TerraformValue.List(
                    TerraformType.String,
                    [
                        TerraformValue.String("one"),
                        TerraformValue.String("two"),
                    ]),
                ["settings"] = TerraformValue.Object(
                    nestedType,
                    new Dictionary<string, TerraformValue>(StringComparer.Ordinal)
                    {
                        ["enabled"] = TerraformValue.Bool(true),
                        ["threshold"] = TerraformValue.Number(TerraformNumber.Parse("3.5")),
                    }),
            });

        var encoded = TerraformDynamicValueSerializer.EncodeDynamicValue(value, valueType);
        var decoded = TerraformDynamicValueSerializer.DecodeDynamicValue(encoded, valueType);

        AssertEquivalent(value, decoded);
    }

    [Fact]
    public void EncodeDecodeMsgPack_PreservesUnknownValue()
    {
        var value = TerraformValue.Unknown(TerraformType.String);

        var encoded = TerraformDynamicValueSerializer.EncodeDynamicValue(value, TerraformType.String);
        var decoded = TerraformDynamicValueSerializer.DecodeDynamicValue(encoded, TerraformType.String);

        Assert.True(decoded.IsUnknown);
        Assert.Equal(TerraformType.String, decoded.Type);
    }

    [Fact]
    public void EncodeDecodeMsgPack_RoundTripsDynamicValue()
    {
        var encoded = TerraformDynamicValueSerializer.EncodeDynamicValue(TerraformValue.String("hello"), TerraformType.Dynamic);
        var decoded = TerraformDynamicValueSerializer.DecodeDynamicValue(encoded, TerraformType.Dynamic);

        Assert.True(decoded.IsKnown);
        Assert.Equal(TerraformType.Dynamic, decoded.Type);

        var innerValue = Assert.IsType<TerraformValue>(decoded.Value);
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

    private static void AssertEquivalent(TerraformValue expected, TerraformValue actual)
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
            case IReadOnlyList<TerraformValue> expectedSequence:
            {
                var actualSequence = actual.AsSequence();
                Assert.Equal(expectedSequence.Count, actualSequence.Count);

                for (var index = 0; index < expectedSequence.Count; index++)
                {
                    AssertEquivalent(expectedSequence[index], actualSequence[index]);
                }

                return;
            }
            case IReadOnlyDictionary<string, TerraformValue> expectedObject:
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
            case TerraformValue expectedInnerValue:
            {
                var actualInnerValue = Assert.IsType<TerraformValue>(actual.Value);
                AssertEquivalent(expectedInnerValue, actualInnerValue);
                return;
            }
            default:
                throw new InvalidOperationException($"Unsupported Terraform test value '{expected.Value?.GetType().Name}'.");
        }
    }
}
