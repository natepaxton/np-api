using NpApi.Api.Features.People;

namespace NpApi.Api.Tests.Features.People;

public class PersonTests
{
    [Theory]
    [InlineData("Nate", null, null, "Nate")]
    [InlineData("Laura", null, "Paxton", "Laura Paxton")]
    [InlineData("Laura", "Ann", "Paxton", "Laura Ann Paxton")]
    [InlineData("Kennedy", "", null, "Kennedy")]
    public void Display_name_joins_the_names_that_are_present(string first, string? middle, string? last, string expected)
    {
        var person = new Person { FirstName = first, MiddleName = middle, LastName = last };

        Assert.Equal(expected, person.DisplayName);
    }
}
