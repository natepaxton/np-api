using NpApi.Api.Features.Places;

namespace NpApi.Api.Tests.Features.Places;

public class PlaceTests
{
    [Theory]
    [InlineData("Gardiner", "Montana", CountryCode.US, "Gardiner, Montana, US")]
    [InlineData(null, "Wyoming", CountryCode.US, "Wyoming, US")]
    [InlineData(null, null, CountryCode.CA, "CA")]
    [InlineData("Toronto", "Ontario", null, "Toronto, Ontario")]
    public void Display_name_joins_the_names_that_are_present(string? city, string? state, CountryCode? country, string expected)
    {
        var place = new Place { City = city, StateProvince = state, CountryCode = country };

        Assert.Equal(expected, place.DisplayName);
    }

    [Fact]
    public void Display_name_falls_back_to_the_point()
    {
        var place = new Place();
        place.SetPoint(44.460512345, -110.8281);

        Assert.Equal("44.46051, -110.8281", place.DisplayName);
    }

    [Fact]
    public void Point_requires_both_halves()
    {
        Assert.Throws<ArgumentException>(() => new Place().SetPoint(44.46, null));
    }

    [Theory]
    [InlineData("US", CountryCode.US)]
    [InlineData("ca", CountryCode.CA)]
    [InlineData(" mx ", CountryCode.MX)]
    public void Country_codes_parse_case_insensitively(string value, CountryCode expected)
    {
        Assert.True(Country.TryParse(value, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("USA")]
    [InlineData("ZZ")]
    [InlineData("1")]
    [InlineData("01")]
    [InlineData("U1")]
    [InlineData("United States")]
    public void Anything_else_is_not_a_country_code(string? value)
    {
        Assert.False(Country.TryParse(value, out _));
    }

    [Fact]
    public void Every_country_code_has_a_name()
    {
        Assert.Equal(Enum.GetValues<CountryCode>().Order(), Country.Names.Keys.Order());
    }
}
