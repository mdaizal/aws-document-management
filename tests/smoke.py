"""Run against a Development API with PostgreSQL and both demo users seeded."""

import http.cookiejar
import json
import os
import urllib.error
import urllib.request
import uuid


BASE_URL = os.environ.get("DOCUMENTS_API_URL", "http://localhost:5107")


class Session:
    def __init__(self):
        self.client = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar())
        )

    def request(self, path, expected, method="GET", data=None, headers=None):
        request = urllib.request.Request(BASE_URL + path, data, headers or {}, method=method)
        try:
            response = self.client.open(request, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            body = response.read()
            assert response.status == expected, (
                f"{method} {path}: expected {expected}, got {response.status}: {body[:300]!r}"
            )
            return body, response.headers

    def token(self):
        body, _ = self.request("/api/auth/antiforgery", 200)
        return json.loads(body)["requestToken"]

    def login(self, name, password):
        self.request(
            "/api/auth/login", 204, "POST",
            json.dumps({"userName": name, "password": password}).encode(),
            {"Content-Type": "application/json", "X-XSRF-TOKEN": self.token()},
        )


def sample_pdf():
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] >>",
    ]
    data = b"%PDF-1.4\n"
    offsets = []
    for number, obj in enumerate(objects, 1):
        offsets.append(len(data))
        data += f"{number} 0 obj\n".encode() + obj + b"\nendobj\n"
    xref = len(data)
    data += b"xref\n0 4\n0000000000 65535 f \n"
    data += b"".join(f"{offset:010} 00000 n \n".encode() for offset in offsets)
    return data + f"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode()


def upload(session, title, tag, content, expected, token=None):
    boundary = uuid.uuid4().hex
    fields = [
        ('name="title"', title.encode()),
        ('name="tags"', tag.upper().encode()),
        ('name="tags"', tag.encode()),
        ('name="file"; filename="sample.pdf"\r\nContent-Type: application/pdf', content),
    ]
    data = b"".join(
        f"--{boundary}\r\nContent-Disposition: form-data; {disposition}\r\n\r\n".encode()
        + value + b"\r\n" for disposition, value in fields
    ) + f"--{boundary}--\r\n".encode()
    headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}
    if token is not None:
        headers["X-XSRF-TOKEN"] = token
    return session.request("/api/documents", expected, "POST", data, headers)


def main():
    alice, bob, anonymous = Session(), Session(), Session()
    anonymous.request("/api/documents", 401)
    if os.environ.get("DOCUMENTS_CHECK_PRODUCTION") == "1":
        anonymous.request("/openapi/v1.json", 404)
        anonymous.request("/scalar/v1", 404)
        print("Production OpenAPI and Scalar routes are disabled.")
        return

    spec, _ = anonymous.request("/openapi/v1.json", 200)
    paths = json.loads(spec)["paths"]
    assert "multipart/form-data" in paths["/api/documents"]["post"]["requestBody"]["content"]
    assert "application/pdf" in paths["/api/documents/{id}/download"]["get"]["responses"]["200"]["content"]
    page, _ = anonymous.request("/scalar/v1", 200)
    assert b"scalar" in page.lower()
    alice.login("alice", os.environ["DemoUsers__AlicePassword"])
    bob.login("bob", os.environ["DemoUsers__BobPassword"])
    title = "Sample " + uuid.uuid4().hex
    tag = uuid.uuid4().hex
    pdf = sample_pdf()
    upload(alice, title, tag, pdf, 400)
    token = alice.token()
    upload(alice, title, tag, b"Not a PDF", 415, token)
    upload(alice, title, tag, pdf + b" " * (10 * 1024 * 1024), 413, token)
    body, _ = alice.request(f"/api/documents?tag={tag}", 200)
    assert json.loads(body) == []
    body, headers = upload(alice, title, tag, pdf, 201, token)
    document = json.loads(body)
    assert document["tags"] == [tag]
    path = headers["Location"]
    for query in (f"title={title.split()[1].upper()}", f"tag={tag.upper()}"):
        body, _ = alice.request(f"/api/documents?{query}", 200)
        assert [item["id"] for item in json.loads(body)] == [document["id"]]
        body, _ = bob.request(f"/api/documents?{query}", 200)
        assert json.loads(body) == []
    alice.request(path, 200)
    bob.request(path, 404)
    bob.request(path + "/download", 404)
    body, headers = alice.request(path + "/download", 200)
    assert body == pdf
    assert headers.get_content_type() == "application/pdf"
    assert headers.get_content_disposition() == "attachment"
    alice.request("/api/auth/logout", 204, "POST", headers={"X-XSRF-TOKEN": token})
    alice.request(path + "/download", 401)
    print("Passed Scalar/OpenAPI, login, antiforgery, upload validation, search, download, and access denial.")


if __name__ == "__main__":
    main()
