using System.Collections.ObjectModel;

namespace TerraformPluginDotnet.Types;

public sealed class TerraformAttributePath : IEquatable<TerraformAttributePath>
{
    private readonly ReadOnlyCollection<TerraformAttributePathStep> _steps;

    public TerraformAttributePath(IEnumerable<TerraformAttributePathStep> steps)
    {
        _steps = steps.ToList().AsReadOnly();
    }

    public IReadOnlyList<TerraformAttributePathStep> Steps => _steps;

    public static TerraformAttributePath Root(string attributeName) =>
        new([TerraformAttributePathStep.Attribute(attributeName)]);

    public TerraformAttributePath WithAttribute(string attributeName) =>
        new(_steps.Concat([TerraformAttributePathStep.Attribute(attributeName)]));

    public TerraformAttributePath WithElementKey(string key) =>
        new(_steps.Concat([TerraformAttributePathStep.Element(key)]));

    public TerraformAttributePath WithElementIndex(long index) =>
        new(_steps.Concat([TerraformAttributePathStep.Element(index)]));

    public bool Equals(TerraformAttributePath? other)
    {
        if (other is null || other._steps.Count != _steps.Count)
        {
            return false;
        }

        for (var index = 0; index < _steps.Count; index++)
        {
            if (!_steps[index].Equals(other._steps[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as TerraformAttributePath);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var step in _steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        string.Join(".", _steps.Select(
            step => step.Selector switch
            {
                TerraformAttributePathSelector.AttributeName => step.AttributeName!,
                TerraformAttributePathSelector.ElementKeyInt => step.ElementIndex!.Value.ToString(),
                TerraformAttributePathSelector.ElementKeyString => $"[{step.ElementKeyString}]",
                _ => "<?>",
            }));
}

public readonly record struct TerraformAttributePathStep(
    TerraformAttributePathSelector Selector,
    string? AttributeName,
    string? ElementKeyString,
    long? ElementIndex)
{
    public static TerraformAttributePathStep Attribute(string attributeName) =>
        new(TerraformAttributePathSelector.AttributeName, attributeName, null, null);

    public static TerraformAttributePathStep Element(string key) =>
        new(TerraformAttributePathSelector.ElementKeyString, null, key, null);

    public static TerraformAttributePathStep Element(long index) =>
        new(TerraformAttributePathSelector.ElementKeyInt, null, null, index);
}

public enum TerraformAttributePathSelector
{
    AttributeName,
    ElementKeyString,
    ElementKeyInt,
}
