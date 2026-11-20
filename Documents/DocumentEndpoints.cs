using System.Security.Claims;
using aws_document_management.Data;
using aws_document_management.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aws_document_management.Documents;

internal static class DocumentEndpoints
{
    private const long FileLimit = 10 * 1024 * 1024;
    internal const long RequestLimit = FileLimit + 128 * 1024;
    private const int PageSize = 25;

    public static void MapDocuments(this IEndpointRouteBuilder endpoints)
    {
        var documents = endpoints.MapGroup("/api/documents").WithTags("Documents").RequireAuthorization();
        documents.MapPost("", UploadAsync).DocumentAntiforgeryHeader().Produces<DocumentResponse>(201)
            .ProducesProblem(400).Produces(401).ProducesProblem(413).ProducesProblem(415)
            .WithSummary("Upload a sample PDF")
            .WithDescription("Provide a PDF up to 10 MiB, a title, and optional repeated tags. Send the session cookie and X-XSRF-TOKEN header.");
        documents.MapGet("", SearchAsync).Produces<DocumentResponse[]>().Produces(401).ProducesProblem(400)
            .WithSummary("Search your documents by title substring or exact tag")
            .WithDescription("Filters are case-insensitive and combined when both are provided. Pages start at 1.");
        documents.MapGet("/{id:guid}", GetAsync).Produces<DocumentResponse>()
            .Produces(401).Produces(404).WithSummary("Read your document metadata");
        documents.MapGet("/{id:guid}/download", DownloadAsync).Produces<Stream>(200, contentType: "application/pdf")
            .Produces(401).Produces(404).WithSummary("Download your document as a PDF attachment");
    }

    private static async Task<IResult> UploadAsync([FromForm] UploadForm form, ClaimsPrincipal user,
        DocumentDbContext db,
        IDocumentStorage storage, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var title = form.Title?.Trim();
        var tags = form.Tags;
        var file = form.File;
        if (file is null)
            return Results.Problem(statusCode: 400, title: "A PDF file is required.");
        if (string.IsNullOrEmpty(title) || title.Length > 200 || tags is { Length: > 10 })
            return Results.Problem(statusCode: 400, title: "Provide a title of up to 200 characters and up to ten tags.");

        var normalizedTags = (tags ?? []).Select(NormalizeTag).Distinct().ToArray();
        if (normalizedTags.Any(tag => tag.Length is 0 or > 40))
            return Results.Problem(statusCode: 400, title: "Tags must contain 1 to 40 characters.");

        if (file.Length > FileLimit)
            return Results.Problem(statusCode: 413, title: "PDFs must be 10 MiB or smaller.");

        var fileName = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (fileName.Length is 0 or > 255 || fileName.Any(char.IsControl))
            return Results.Problem(statusCode: 400, title: "Invalid file name.");

        await using var content = file.OpenReadStream();
        var signature = new byte[5];
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || await content.ReadAtLeastAsync(signature, signature.Length, false,
                cancellationToken) != signature.Length
            || !signature.AsSpan().SequenceEqual("%PDF-"u8))
            return Results.Problem(statusCode: 415, title: "Upload a valid PDF file.");
        content.Position = 0;

        var document = new Document
        {
            OwnerId = UserId(user), Title = title, FileName = fileName, Size = file.Length,
            Tags = normalizedTags.Select(value => new DocumentTag { Value = value }).ToList()
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        await storage.SaveAsync(document.Id, content, cancellationToken);

        document.Ready = true;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // A lost acknowledgement can hide a successful commit. Preserve the PDF for reconciliation.
            loggerFactory.CreateLogger("DocumentUpload").LogWarning(exception,
                "Check pending upload {DocumentId} before removing its file.", document.Id);
            throw;
        }

        return Results.Created($"/api/documents/{document.Id}", ToResponse(document));
    }

    private static async Task<IResult> SearchAsync(string? title, string? tag, int? page,
        ClaimsPrincipal user, DocumentDbContext db, CancellationToken cancellationToken)
    {
        var pageNumber = page ?? 1;
        if (pageNumber is < 1 or > 10_000 || title?.Length > 200 || tag?.Length > 40)
            return Results.Problem(statusCode: 400, title: "Invalid search filter or page number.");

        var query = OwnedDocuments(db, user);
        if (!string.IsNullOrWhiteSpace(title))
        {
            var pattern = "%" + title.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(document => EF.Functions.ILike(document.Title, pattern, "\\"));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalizedTag = NormalizeTag(tag);
            query = query.Where(document => document.Tags.Any(item => item.Value == normalizedTag));
        }

        var documents = await query.Include(document => document.Tags)
            .OrderByDescending(document => document.CreatedAt).ThenBy(document => document.Id)
            .Skip((pageNumber - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
        return Results.Ok(documents.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, DocumentDbContext db,
        CancellationToken cancellationToken)
    {
        var document = await OwnedDocuments(db, user).Include(item => item.Tags)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return document is null ? Results.NotFound() : Results.Ok(ToResponse(document));
    }

    private static async Task<IResult> DownloadAsync(Guid id, ClaimsPrincipal user, DocumentDbContext db,
        IDocumentStorage storage, HttpContext http, CancellationToken cancellationToken)
    {
        var document = await OwnedDocuments(db, user).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
            return Results.NotFound();

        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(storage.Open(id), "application/pdf", document.FileName);
    }

    private static IQueryable<Document> OwnedDocuments(DocumentDbContext db, ClaimsPrincipal user)
    {
        var ownerId = UserId(user);
        return db.Documents.AsNoTracking().Where(document => document.Ready && document.OwnerId == ownerId);
    }

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static string NormalizeTag(string value) => value.Trim().ToLowerInvariant();
    private static DocumentResponse ToResponse(Document document) => new(document.Id, document.Title,
        document.FileName, document.Size, document.CreatedAt, document.Tags.Select(tag => tag.Value).Order().ToArray());

    internal sealed class UploadForm
    {
        public IFormFile? File { get; init; }
        public string? Title { get; init; }
        public string[]? Tags { get; init; }
    }

    internal sealed record DocumentResponse(Guid Id, string Title, string FileName, long Size,
        DateTimeOffset CreatedAt, string[] Tags);
}
