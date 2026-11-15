# PantryPal future tasks

Rule: any future task that is not already part of an implementation plan goes in this file. Mark done with [x] and remove when no longer useful.

## Security (next to fix, high importance)
- [ ] Add security headers: CSP, `X-Content-Type-Options: nosniff`, `frame-ancestors 'none'`, Referrer-Policy
- [ ] Set `AllowedHosts` to `pantrypal-dev.duckdns.org` instead of `*`
- [ ] Rate limit receipt parsing and `/api/health/database`
- [ ] Add an upload size limit and a magic-byte check on receipt images
- [ ] Use a least-privilege MySQL user, with TLS; rotate the password if a real one was shared
- [ ] Set `ForwardedHeaders.KnownProxies`
- [ ] Limit CORS to development; confirm `ASPNETCORE_ENVIRONMENT=Production` on the host
- [ ] Fix login timing and the register 409 account enumeration; add per-account login throttling
- [ ] Use SSH keys instead of a password in `scripts/deploy.sh`
- [ ] Run `npm audit` and `dotnet list package --vulnerable`
- [x] Per-user data isolation (add user ID to purchases and scope summary/tag/food queries)
- [x] Set auth cookie `SecurePolicy` to `Always` in backend/Program.cs
- [x] Enforce HTTPS with a redirect and HSTS, or confirm the proxy does both (proxy redirects; HSTS added in app, max-age 1 day)

## Accounts (deferred from the initial accounts/sign-in plan)
- [ ] Email change
- [ ] Password change and password reset
- [ ] Email verification at sign-up
- [ ] Account deletion
- [ ] Admin UI for creating and managing invite codes (currently inserted via SQL)
- [ ] Drop the unused `api_keys` table once accounts are live
