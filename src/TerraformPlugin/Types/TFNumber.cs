using System.Globalization;

namespace TerraformPlugin.Types;

public readonly record struct TFNumber(string Raw)
{
    public static TFNumber FromInt64(long value) =>
        new(value.ToString(CultureInfo.InvariantCulture));

    public static TFNumber FromUInt64(ulong value) =>
        new(value.ToString(CultureInfo.InvariantCulture));

    public static TFNumber FromDouble(double value) =>
        new(value.ToString("R", CultureInfo.InvariantCulture));

    public static TFNumber Parse(string raw) => new(raw);

    public bool TryGetInt64(out long value) =>
        long.TryParse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public bool TryGetUInt64(out ulong value) =>
        ulong.TryParse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public bool TryGetDouble(out double value) =>
        double.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public override string ToString() => Raw;
}
