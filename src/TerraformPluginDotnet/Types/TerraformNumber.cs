using System.Globalization;

namespace TerraformPluginDotnet.Types;

public readonly record struct TerraformNumber(string Raw)
{
    public static TerraformNumber FromInt64(long value) =>
        new(value.ToString(CultureInfo.InvariantCulture));

    public static TerraformNumber FromUInt64(ulong value) =>
        new(value.ToString(CultureInfo.InvariantCulture));

    public static TerraformNumber FromDouble(double value) =>
        new(value.ToString("R", CultureInfo.InvariantCulture));

    public static TerraformNumber Parse(string raw) => new(raw);

    public bool TryGetInt64(out long value) =>
        long.TryParse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public bool TryGetUInt64(out ulong value) =>
        ulong.TryParse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public bool TryGetDouble(out double value) =>
        double.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public override string ToString() => Raw;
}
