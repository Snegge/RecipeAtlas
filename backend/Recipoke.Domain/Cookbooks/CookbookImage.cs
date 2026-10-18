namespace Recipoke.Domain.Cookbooks;

public sealed class CookbookImage
{
    public Guid CookbookId { get; set; }
    public byte[] Bytes { get; set; } = [];
    public string ContentType { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
}
