using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Types;
using System.Globalization;

namespace TerraformPluginDotnet.Tests;

public sealed class TerraformModelBinderTests
{
    [Fact]
    public void RoundTrip_UsesConventionBasedNamesAndNestedBlocks()
    {
        var model = new ConventionResourceModel
        {
            DisplayName = TF<string>.Known("primary"),
            Timeouts = new TimeoutsBlock
            {
                Create = TF<string>.Known("30m"),
                Delete = null,
            },
        };

        var value = TerraformModelBinder.Unbind(model);
        var attributes = value.AsObject();

        Assert.Contains("display_name", attributes.Keys);
        Assert.Contains("timeouts", attributes.Keys);

        var timeouts = attributes["timeouts"].AsObject();
        Assert.Contains("create", timeouts.Keys);
        Assert.Contains("delete", timeouts.Keys);
        Assert.True(timeouts["delete"].IsNull);

        var roundTrip = TerraformModelBinder.Bind<ConventionResourceModel>(value);

        Assert.Equal("primary", roundTrip.DisplayName.RequireValue());
        Assert.NotNull(roundTrip.Timeouts);
        Assert.Equal("30m", roundTrip.Timeouts.Create?.RequireValue());
        Assert.Null(roundTrip.Timeouts.Delete);
    }

    [Fact]
    public void RoundTrip_UsesInvariantCultureForNumbers()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var model = new NumericModel
            {
                Count = TF<int>.Known(42),
                Ratio = TF<double>.Known(3.5),
            };

            var value = TerraformModelBinder.Unbind(model);
            var roundTrip = TerraformModelBinder.Bind<NumericModel>(value);

            Assert.Equal("42", value.GetAttribute("count").AsNumber().Raw);
            Assert.Equal("3.5", value.GetAttribute("ratio").AsNumber().Raw);
            Assert.Equal(42, roundTrip.Count.RequireValue());
            Assert.Equal(3.5, roundTrip.Ratio.RequireValue());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private sealed class ConventionResourceModel
    {
        public TF<string> DisplayName { get; init; }

        public TimeoutsBlock Timeouts { get; init; } = new();
    }

    private sealed class TimeoutsBlock
    {
        public TF<string>? Create { get; init; }

        public TF<string>? Delete { get; init; }
    }

    private sealed class NumericModel
    {
        public TF<int> Count { get; init; }

        public TF<double> Ratio { get; init; }
    }
}
