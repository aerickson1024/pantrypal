# PantryPal

A minimal Angular frontend and ASP.NET Core API with Gemini-powered grocery receipt extraction and a local MySQL connectivity check.

## Requirements

- Node.js and npm
- .NET 10 SDK
- MySQL 8.0 or later
- A Gemini API key

## Configure Gemini

Store your key in .NET User Secrets from the repository root:

```sh
dotnet user-secrets set "Gemini:ApiKey" "<your Gemini API key>" --project backend
```

The key stays on the backend and is not committed to source control. Restart the backend after changing the secret.

## Configure local MySQL

Create the database and MySQL user locally, then store the connection string in .NET User Secrets:

```sh
dotnet user-secrets set "ConnectionStrings:PantryPal" "<your MySQL connection string>" --project backend
```

Use the connection string format `Server=<host>;Port=<port>;Database=<database>;User ID=<username>;Password=<password>`. Replace the placeholders with your local values; keep the real connection string out of source control and chat. The existing database must be available to the configured MySQL account. Restart the backend after changing the secret.

The database connectivity check is at http://localhost:5044/api/health/database. It returns `200` when MySQL is reachable and a sanitized `503` otherwise. The general `/api/health` endpoint checks only that the API is running.

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

Each displayed line is saved separately, even when multiple lines reference the same food. Repeating an identical hash and ordered item list is idempotent; reusing a hash with different purchase data returns `409`. Re-uploading the same exact image file is treated as the same receipt; different image bytes produce a different hash. The general health check is at http://localhost:5044/api/health.