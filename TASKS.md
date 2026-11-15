# PantryPal future tasks

Rule: any future task that is not already part of an implementation plan goes in this file. Mark done with [x] and remove when no longer useful.

## Security (next to fix, high importance)
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
