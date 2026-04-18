using System.Text;
using System.Text.Json;

namespace TerraformPluginDotnet.Types;

public abstract class TerraformType : IEquatable<TerraformType>
{
    public static TerraformPrimitiveType String { get; } = new("string");
    public static TerraformPrimitiveType Number { get; } = new("number");
    public static TerraformPrimitiveType Bool { get; } = new("bool");
    public static TerraformPrimitiveType Dynamic { get; } = new("dynamic");

    public abstract string ToTypeJson();

    public virtual byte[] ToTypeBytes() => Encoding.UTF8.GetBytes(ToTypeJson());

    public static TerraformType ParseTypeJson(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        return ParseTypeJson(document.RootElement);
    }

    private static TerraformType ParseTypeJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() switch
            {
                "string" => String,
                "number" => Number,
                "bool" => Bool,
                "dynamic" => Dynamic,
                var value => throw new InvalidOperationException($"Unsupported Terraform primitive type '{value}'."),
            };
        }

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 2)
        {
            throw new InvalidOperationException("Invalid Terraform type JSON.");
        }

        var kind = element[0].GetString() ?? throw new InvalidOperationException("Missing Terraform complex type kind.");

        return kind switch
        {
            "list" => new TerraformListType(ParseTypeJson(element[1])),
            "set" => new TerraformSetType(ParseTypeJson(element[1])),
            "map" => new TerraformMapType(ParseTypeJson(element[1])),
            "tuple" => new TerraformTupleType(element[1].EnumerateArray().Select(ParseTypeJson).ToArray()),
            "object" => ParseObjectType(element),
            _ => throw new InvalidOperationException($"Unsupported Terraform complex type '{kind}'."),
        };
    }

    private static TerraformObjectType ParseObjectType(JsonElement element)
    {
        var attributeTypes = new Dictionary<string, TerraformType>(StringComparer.Ordinal);

        foreach (var property in element[1].EnumerateObject())
        {
            attributeTypes[property.Name] = ParseTypeJson(property.Value);
        }

        var optionalAttributes = new HashSet<string>(StringComparer.Ordinal);

        if (element.GetArrayLength() > 2)
        {
            foreach (var optional in element[2].EnumerateArray())
            {
                optionalAttributes.Add(optional.GetString() ?? string.Empty);
            }
        }

        return new TerraformObjectType(attributeTypes, optionalAttributes);
    }

    public static TerraformType CommonType(IEnumerable<TerraformValue> values)
    {
        TerraformType? current = null;

        foreach (var value in values)
        {
            if (current is null)
            {
                current = value.Type;
                continue;
            }

            if (!current.Equals(value.Type))
            {
                throw new InvalidOperationException($"Mixed Terraform collection element types are not supported: '{current}' and '{value.Type}'.");
            }
        }

        return current ?? Dynamic;
    }

    public bool Equals(TerraformType? other) =>
        other is not null && StringComparer.Ordinal.Equals(ToTypeJson(), other.ToTypeJson());

    public override bool Equals(object? obj) => obj is TerraformType other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToTypeJson());

    public override string ToString() => ToTypeJson();
}

public sealed class TerraformPrimitiveType(string kind) : TerraformType
{
    public string Kind { get; } = kind;

    public override string ToTypeJson() => JsonSerializer.Serialize(Kind);
}

public sealed class TerraformListType(TerraformType elementType) : TerraformType
{
    public TerraformType ElementType { get; } = elementType;

    public override string ToTypeJson() => $"[\"list\",{ElementType.ToTypeJson()}]";
}

public sealed class TerraformSetType(TerraformType elementType) : TerraformType
{
    public TerraformType ElementType { get; } = elementType;

    public override string ToTypeJson() => $"[\"set\",{ElementType.ToTypeJson()}]";
}

public sealed class TerraformMapType(TerraformType elementType) : TerraformType
{
    public TerraformType ElementType { get; } = elementType;

    public override string ToTypeJson() => $"[\"map\",{ElementType.ToTypeJson()}]";
}

public sealed class TerraformTupleType(IReadOnlyList<TerraformType> elementTypes) : TerraformType
{
    public IReadOnlyList<TerraformType> ElementTypes { get; } = elementTypes;

    public override string ToTypeJson() =>
        $"[\"tuple\",[{string.Join(",", ElementTypes.Select(static type => type.ToTypeJson()))}]]";
}

public sealed class TerraformObjectType(
    IReadOnlyDictionary<string, TerraformType> attributeTypes,
    IReadOnlySet<string>? optionalAttributes = null) : TerraformType
{
    public IReadOnlyDictionary<string, TerraformType> AttributeTypes { get; } = attributeTypes;
    public IReadOnlySet<string> OptionalAttributes { get; } = optionalAttributes ?? new HashSet<string>(StringComparer.Ordinal);

    public override string ToTypeJson()
    {
        var attributes = string.Join(
            ",",
            AttributeTypes.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{JsonSerializer.Serialize(pair.Key)}:{pair.Value.ToTypeJson()}"));

        if (OptionalAttributes.Count == 0)
        {
            return $"[\"object\",{{{attributes}}}]";
        }

        var optional = string.Join(
            ",",
            OptionalAttributes.OrderBy(static value => value, StringComparer.Ordinal).Select(static value => JsonSerializer.Serialize(value)));

        return $"[\"object\",{{{attributes}}},[{optional}]]";
    }
}
