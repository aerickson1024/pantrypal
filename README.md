# PantryPal

PantryPal tracks what you buy. Upload a grocery receipt, and Gemini extracts the line items. Each item is matched to a canonical food name, and the purchases are saved. A pantry summary shows quantities and spending by food, and food items can be tagged.

- Accounts are invite-only, and each user's data is private.
- Each user saves their own Gemini API key on the **Account** page.

## Requirements

- Node.js and npm
- .NET 10 SDK
- MySQL 8.0 or later
- A Gemini API key for each user

## Project layout

- `backend/`: ASP.NET Core API using minimal APIs and EF Core with MySQL. It also serves the built frontend.
- `frontend/`: Angular app.
- `scripts/deploy.sh`: builds, uploads and restarts the service.
- `TASKS.md`: outstanding work.

## Local setup

The schema is not generated automatically. Create the tables in MySQL to match the entities and mapping in [backend/Data/PantryPalDbContext.cs](backend/Data/PantryPalDbContext.cs).

Store the connection string in .NET User Secrets so it stays out of source control:

```sh
dotnet user-secrets set "ConnectionStrings:PantryPal" "<connection string>" --project backend
```

Format: `Server=<host>;Port=<port>;Database=<database>;User ID=<username>;Password=<password>`. Restart the backend after changing the secret.

Create an invite code for the first account:

```sh
dotnet run --project backend -- create-invite
```

The command prints a GUID once. Each invite code works once. Share `http://localhost:4200/?invite=<guid>` to open the sign-up form with the code filled in.

## Run

Start the API from the repository root:

```sh
dotnet run --project backend
```

Start the frontend in another terminal:

```sh
cd frontend
npm start
```

Open http://localhost:4200, choose a receipt image, and select **Parse receipt**. JPEG, PNG, WebP, HEIC, and HEIF images up to 10 MB are accepted.

During development, `npm start` forwards `/api` requests to `http://localhost:5044` using [frontend/proxy.conf.json](frontend/proxy.conf.json), so the API must run on that port. CORS and OpenAPI are enabled only in the Development environment.

Health checks: `GET /api/health` confirms the API is running. `GET /api/health/database` returns `200` when MySQL is reachable and `503` otherwise.

## API overview

All endpoints require a signed-in session except account registration and login, and the health checks.

| Method and path | Purpose |
| --- | --- |
| `POST /api/account/register` | Create an account with an invite code |
| `POST /api/account/login` | Sign in |
| `POST /api/account/logout` | Sign out |
| `GET` and `PUT /api/account/me` | Read or update the signed-in profile |
| `GET`, `PUT`, and `DELETE /api/account/gemini-key` | Manage your Gemini API key |
| `POST /api/gemini/receipts/parse` | Extract items from a receipt image |
| `GET /api/food-items/search?q=` | Suggest canonical food names |
| `POST /api/food-items` | Create or reuse a food item |
| `POST /api/food-items/aliases` | Save a corrected receipt name as an alias |
| `POST /api/purchases` | Save a parsed receipt |
| `GET /api/purchases/summary` | Summarize purchases |
| `GET /api/tags` | List tags in use |
| `PUT /api/food-items/{foodItemId}/tags` | Replace a food item's tags |

## Receipts and purchases

The receipt API accepts `POST http://localhost:5044/api/gemini/receipts/parse` as `multipart/form-data` with the image in the `image` field. It returns a top-level `hash` and an `items` array; each item has these JSON properties:

```json
{
	"hash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
	"items": [
		{
			"item name": "Apples",
			"quantity purchased": 2,
			"date of purchase": "2026-11-07",
			"cost per item": 1.49,
			"aliasMatched": false,
			"foodItemId": null
		}
	]
}
```

The `hash` is a lowercase SHA-256 digest of the uploaded image bytes. Re-uploading the exact same file produces the same hash regardless of Gemini's extraction; a re-encoded or otherwise changed image has a different hash. The purchase date is an ISO date (`YYYY-MM-DD`), quantities and costs are numbers, and receipt interactions are not stored by Gemini. When an item's name matches a stored alias, the API returns its canonical food item name with `aliasMatched: true` and its string `foodItemId`; unmatched names are returned unchanged with `aliasMatched: false` and a null `foodItemId`. Alias matching ignores letter case but distinguishes accents and requires a full-value match.

An unmatched receipt name can be corrected with `POST http://localhost:5044/api/food-items/aliases` using `originalName` (the Gemini text) and `canonicalName` (the corrected name). The API reuses an existing food item or creates it when needed, then stores the original name as an alias. Its success response contains `canonicalName` and string `foodItemId`.

While correcting an unmatched name, the app searches canonical food names after two seconds without typing. Case-insensitive prefix matches are returned first, followed by up to eight fuzzy alternatives. For example, `GET http://localhost:5044/api/food-items/search?q=ruf` can return `Ruffles Sour Cream and Cheddar`. Suggestions do not change or save the receipt row until selected; custom names can still be entered manually.

After every row has a canonical food item, save the table with `POST http://localhost:5044/api/purchases`:

```json
{
	"hash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
	"items": [
		{
			"foodItemId": "1",
			"dateOfPurchase": "2026-11-07",
			"quantityPurchased": 2,
			"costPerItem": 1.49
		}
	]
}
```

Each displayed line is saved separately, even when multiple lines reference the same food. Repeating an identical hash and ordered item list is idempotent; reusing a hash with different purchase data returns `409`. Re-uploading the same exact image file is treated as the same receipt; different image bytes produce a different hash.

Tags are shared across a user's food items. `GET /api/tags` lists tags in use. `PUT /api/food-items/{foodItemId}/tags` replaces a food item's full tag set, for example `{ "tags": ["Drinks", "Pop"] }`. Tag names are trimmed, limited to 100 characters, and matched case-insensitively. Tags with no food items are removed.

## Deploy

The API serves the built Angular app, so one process hosts the UI at `/` and the API at `/api`. Unknown non-API paths fall back to `index.html`.

1. Build the frontend with `npm run build` in `frontend/`. Output goes to `backend/wwwroot/`.
2. Run `bash scripts/deploy.sh`. It publishes the backend, uploads it over SSH with key authentication, and restarts the service. The host must allow the deploy user to run the required commands without a password.

On the host:

- Set `ASPNETCORE_ENVIRONMENT=Production`.
- Supply `ConnectionStrings__PantryPal` through the environment or a secret store, not `appsettings.json`.
- Set `DataProtection__KeysPath` to a persistent directory outside the deploy folder. Keep it across deploys.
- Set `AllowedHosts` to the public hostname.
- Serve the app over HTTPS through a reverse proxy that forwards to the API on the loopback address.