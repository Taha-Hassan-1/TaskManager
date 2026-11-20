# 12. API contract

**Status:** Accepted, with a tool change

**Decision.** The built-in `Microsoft.AspNetCore.OpenApi` document, a generated TypeScript client, Swashbuckle dropped.

**Why.** One contract and no hand-typed DTOs in Angular.

**Rejected.** A hand-written client.

**What happened.** The spec said to confirm NSwag at M4. **`ng-openapi-gen` was used instead**: it is plain npm, while NSwag's OpenAPI 3.1 support is partial and `@hey-api/openapi-ts` needs Node 22.18 or newer. The document is committed as `client/openapi.json`; a backend test fails if it is stale and CI fails if the generated client is. Getting clean types took three server changes (JSON-only media types, enums as names, numbers as numbers) so the document describes what is really sent.
