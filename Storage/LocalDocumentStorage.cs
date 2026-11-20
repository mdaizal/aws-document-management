namespace aws_document_management.Storage;

internal sealed class LocalDocumentStorage(IWebHostEnvironment environment, ILogger<LocalDocumentStorage> logger)
    : IDocumentStorage
{
    private readonly string directory = Path.Combine(environment.ContentRootPath, ".local", "documents");

    public async Task SaveAsync(Guid id, Stream source, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var target = GetPath(id);
        var temporary = target + ".partial";

        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(file, cancellationToken);
            }
            File.Move(temporary, target);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not clean up partial upload {DocumentId}.", id);
            }
            throw;
        }
    }

    public Stream Open(Guid id) => File.OpenRead(GetPath(id));

    private string GetPath(Guid id) => Path.Combine(directory, id.ToString("N") + ".pdf");
}
