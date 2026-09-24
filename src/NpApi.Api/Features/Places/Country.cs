using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NpApi.Api.Features.Places;

// ISO 3166-1 alpha-2 codes. The member names are the codes and are stored as text ("US"), so
// never rename a member. Adding a country: add a member here and a name in Country.Names, then
// `dotnet ef migrations add` seeds the new row.
public enum CountryCode
{
    US,
    CA,
    MX,
}

// Lookup table mirroring CountryCode, so the database rejects any other code (places.country_code
// is a foreign key to it) and clients can list the supported countries.
public sealed class Country
{
    public const int CodeLength = 2;

    public static readonly IReadOnlyDictionary<CountryCode, string> Names = new Dictionary<CountryCode, string>
    {
        [CountryCode.US] = "United States",
        [CountryCode.CA] = "Canada",
        [CountryCode.MX] = "Mexico",
    };

    public CountryCode Code { get; init; }
    public required string Name { get; init; }

    // Accepts "US" or "us"; rejects anything that isn't a defined code, including numbers like "1"
    // that Enum.TryParse would otherwise turn into a member.
    public static bool TryParse(string? value, out CountryCode code)
    {
        code = default;
        var trimmed = value?.Trim();
        return trimmed is { Length: CodeLength }
            && trimmed.All(char.IsAsciiLetter)
            && Enum.TryParse(trimmed, ignoreCase: true, out code)
            && Enum.IsDefined(code);
    }
}

internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries");
        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).HasConversion<string>().HasMaxLength(Country.CodeLength).IsFixedLength();
        builder.Property(c => c.Name).HasMaxLength(100);

        // One row per enum member, generated from the enum so the two can't drift apart.
        builder.HasData(Enum.GetValues<CountryCode>().Select(code => new Country { Code = code, Name = Country.Names[code] }));
    }
}
