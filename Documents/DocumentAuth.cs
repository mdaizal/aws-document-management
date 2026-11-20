using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi;

namespace aws_document_management.Documents;

internal static class DocumentAuth
{
    public static void MapDocumentAuth(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth").WithTags("Session");
        auth.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var token = antiforgery.GetAndStoreTokens(http).RequestToken!;
            return TypedResults.Ok(new AntiforgeryToken(token));
        }).WithSummary("Get a token to send as X-XSRF-TOKEN")
            .WithDescription("Keep the browser cookies and request a new token after signing in.");

        auth.MapPost("/login", async (LoginRequest request, SignInManager<IdentityUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
                return Results.Unauthorized();

            var result = await signIn.PasswordSignInAsync(request.UserName, request.Password,
                isPersistent: false, lockoutOnFailure: true);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        }).AddEndpointFilter(CheckAntiforgery).DocumentAntiforgeryHeader().Produces(204).Produces(401).ProducesProblem(400)
            .WithSummary("Sign in and receive the session cookie")
            .WithDescription("Send the antiforgery cookie and X-XSRF-TOKEN header.");

        auth.MapPost("/logout", async (SignInManager<IdentityUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).RequireAuthorization().AddEndpointFilter(CheckAntiforgery).DocumentAntiforgeryHeader().Produces(401).ProducesProblem(400)
            .WithSummary("End the current session")
            .WithDescription("Send the current session's X-XSRF-TOKEN header.");

        auth.MapGet("/me", (HttpContext http) => TypedResults.Ok(new SessionUser(http.User.Identity!.Name!)))
            .RequireAuthorization().Produces(401).WithSummary("Read the signed-in user name");
    }

    internal static RouteHandlerBuilder DocumentAntiforgeryHeader(this RouteHandlerBuilder endpoint)
    {
        return endpoint.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-XSRF-TOKEN",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Token from GET /api/auth/antiforgery for the current browser session.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
            return Task.CompletedTask;
        });
    }

    private static async ValueTask<object?> CheckAntiforgery(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        return await antiforgery.IsRequestValidAsync(http)
            ? await next(context)
            : Results.Problem(statusCode: 400, title: "Invalid or missing antiforgery token.");
    }

    internal sealed record LoginRequest(string UserName, string Password);
    internal sealed record AntiforgeryToken(string RequestToken);
    internal sealed record SessionUser(string UserName);
}
