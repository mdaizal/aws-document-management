# AWS Document Management

Milestone 1 is a local ASP.NET Core API for uploading, finding, and downloading sample PDFs. PostgreSQL stores accounts and metadata. PDF bytes live in the ignored `.local/documents/` directory. The API checks document ownership before returning metadata or file bytes.

## Run locally

Use .NET 10, Docker with Compose, and Python 3 for the integration smoke test. Run these commands from the repository root in Bash. Choose a new development password for each prompt. Identity expects at least six characters including uppercase, lowercase, a digit, and punctuation.

```bash
read -rsp 'Local PostgreSQL password (use hex characters): ' DOCUMENTS_DB_PASSWORD; echo
read -rsp 'Alice demo password: ' DemoUsers__AlicePassword; echo
read -rsp 'Bob demo password: ' DemoUsers__BobPassword; echo
export DOCUMENTS_DB_PASSWORD DemoUsers__AlicePassword DemoUsers__BobPassword
export ConnectionStrings__Documents="Host=127.0.0.1;Port=${DOCUMENTS_DB_PORT:-5432};Database=documents;Username=documents;Password=$DOCUMENTS_DB_PASSWORD"

docker compose up -d --wait postgres
dotnet tool restore
dotnet build
dotnet ef database update
dotnet run --launch-profile http -- --seed-demo-users
dotnet run --launch-profile http
```

Keep the same database password when reusing the local Docker volume. Set and export `DOCUMENTS_DB_PORT` before startup if the default port is occupied. Seeding creates missing demo users and preserves existing passwords.

## Explore and verify

- Open Scalar at `http://localhost:5107/scalar/v1`. The OpenAPI document is at `/openapi/v1.json`. Both routes are Development-only.
- In Scalar, request `GET /api/auth/antiforgery`. Use the returned token as the `X-XSRF-TOKEN` header on `POST /api/auth/login` with Alice's username and local password. The browser stores the session cookie. Fetch a fresh token before subsequent writes. Run `GET /api/documents` using that same browser session. The request examples in `aws-document-management.http` use REST Client syntax. Configure an ignored .env file for the demo password and retain cookies between requests.
- In a second terminal with both demo passwords exported, run `python3 tests/smoke.py`. It uploads a synthetic PDF, checks title and tag queries and exact download bytes, and verifies that Bob cannot find or retrieve Alice's PDF. Use `DOCUMENTS_API_URL` to override the default API address.
- Stop the API with Ctrl+C and PostgreSQL with `docker compose stop postgres`. The Docker volume holds the database and `.local/documents/` holds the PDFs.

An upload stays hidden until its file has been written and its database row is marked ready. A failed or interrupted upload can leave a pending row or file for inspection. Automated reconciliation, sharing grants, Angular, and S3 are later milestones.
