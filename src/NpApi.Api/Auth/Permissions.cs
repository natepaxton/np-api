namespace NpApi.Api.Auth;

// Auth0 API permissions (infra/auth0 api_permissions). Each is also an authorization policy of
// the same name, so endpoints use .RequireAuthorization(Permissions.ReadPhotos).
public static class Permissions
{
    public const string ReadPhotos = "read:photos";
    public const string WritePhotos = "write:photos";
    public const string ReadPeople = "read:people";
    public const string WritePeople = "write:people";

    public static readonly IReadOnlyList<string> All = [ReadPhotos, WritePhotos, ReadPeople, WritePeople];
}
