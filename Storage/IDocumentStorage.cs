namespace aws_document_management.Storage;

internal interface IDocumentStorage
{
    Task SaveAsync(Guid id, Stream source, CancellationToken cancellationToken);
    Stream Open(Guid id);
}
