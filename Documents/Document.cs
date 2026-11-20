namespace aws_document_management.Documents;

public sealed class Document
{
    // The document ID also serves as the opaque local storage key.
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string OwnerId { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public long Size { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool Ready { get; set; }
    public List<DocumentTag> Tags { get; init; } = [];
}

public sealed class DocumentTag
{
    public Guid DocumentId { get; init; }
    public required string Value { get; init; }
}
