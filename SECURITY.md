# Security policy

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Report it privately through GitHub: **Security → Report a vulnerability** on this repository (GitHub private
vulnerability reporting). Include what you found, how to reproduce it, and the SDK version.

You will get an answer within a few working days. We will tell you when a fix is released, and we credit reporters
who want to be named.

## Scope

- This SDK: the code under `unity/com.tupic.sdk` and `dotnet/`.
- How it handles tokens: sign-in, refresh, the host page's token, and log redaction.

Vulnerabilities in Tupic's services themselves can be reported the same way. Never test against another user's
account or data.

## Supported versions

Only the latest released version gets security fixes.
