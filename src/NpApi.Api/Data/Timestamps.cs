namespace NpApi.Api.Data;

public static class Timestamps
{
    // Postgres timestamptz stores microseconds; .NET ticks are 100ns. Truncating before saving
    // keeps the value an endpoint returns identical to what later reads return from the database.
    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    public static DateTimeOffset UtcNow() => TruncateToMicroseconds(DateTimeOffset.UtcNow);
}
