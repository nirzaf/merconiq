# Security scanning

The Security workflow runs on pull requests targeting `master`, pushes to
`master`, manual dispatches, and a weekly Monday schedule. The scheduled run
scans the maintained branch even when no source change triggers CI. It uses
read-only repository contents access plus the narrowly scoped security-events
permission needed to publish SARIF; it does not receive application or
deployment secrets.

Third-party workflow actions in the CI and Security workflows are pinned to
reviewed full commit SHAs with version comments. Dependabot remains
responsible for proposing reviewed updates; a tag or version-only change is
not evidence that the action implementation is unchanged.

- CodeQL performs C# semantic static analysis using a manual Release build.
- Trivy scans the release Docker image built from the repository Dockerfile.
- Fixed HIGH and CRITICAL OS or library vulnerabilities fail the workflow.
- Unfixed findings are reported but do not fail the build because they require
  an upstream patch before the application can remediate them.

Trivy SARIF results are uploaded to GitHub code scanning when available and
retained as a workflow artifact for 14 days, including when enforcement fails.
A missing artifact is not converted into a pass: the scan job preserves its
failure exit code and the artifact step only preserves diagnostics.

CI uploads its coverage directory and generated report with `if: always()` so
a failing test or coverage gate retains diagnostics produced before the
failure. Uploading an artifact never changes a test, security, or coverage
exit code.

Any accepted exception must be documented in the pull request with its
affected package, reason, and a follow-up owner or remediation date; broad
suppressions are not permitted.

## HTTP Security Headers and Reverse Proxy Trust

The application emits defense-in-depth HTTP security headers on all responses:
- **Content-Security-Policy (CSP):** Restricts asset sources to same-origin resources, inline styles and scripts required by the MudBlazor server rendering model, and WebSocket endpoints for interactive Blazor circuits.
- **X-Frame-Options & Anti-Clickjacking:** Configured to deny unauthorized framing of the application interface.
- **X-Content-Type-Options:** Enforced with `nosniff` to prevent MIME-type confusion attacks.
- **Strict-Transport-Security (HSTS):** Enforced on all production HTTPS requests with preload directives.
- **Reverse Proxy and Forwarded Headers:** Forwarded proxy headers (`X-Forwarded-For`, `X-Forwarded-Proto`) are processed strictly from addresses explicitly defined in `Security:TrustedProxies` and `Security:TrustedNetworks`. An empty configuration deliberately rejects untrusted upstream forwarded headers.

