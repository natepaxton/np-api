namespace NpApi.Api.Features.Photos.Storage;

// Cloudinary delivery URLs for the sizes the frontends use. Built from the public ID on read, so
// changing a size is a code change, not a data migration. f_auto serves WebP/AVIF/JPEG per
// browser, which also makes HEIC uploads displayable.
public static class PhotoUrls
{
    public const string ThumbnailTransformation = "c_fill,w_200,h_200,f_auto,q_auto";
    public const string MediumTransformation = "c_limit,w_800,f_auto,q_auto";
    public const string FullTransformation = "f_auto,q_auto";

    public static string Thumbnail(string cloudName, string publicId) => Build(cloudName, ThumbnailTransformation, publicId);
    public static string Medium(string cloudName, string publicId) => Build(cloudName, MediumTransformation, publicId);
    public static string Full(string cloudName, string publicId) => Build(cloudName, FullTransformation, publicId);

    private static string Build(string cloudName, string transformation, string publicId) =>
        $"https://res.cloudinary.com/{cloudName}/image/upload/{transformation}/{publicId}";
}
