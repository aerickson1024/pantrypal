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

The receipt API accepts `POST http://localhost:5044/api/gemini/receipts/parse` as `multipart/form-data` with the image in the `image` field. It returns an `items` array; each item has these exact JSON properties:

```json
{
	"items": [
		{
			"item name": "Apples",
			"quantity purchased": 2,
			"date of purchase": "2026-11-07",
			"cost per item": 1.49,
			"aliasMatched": false
		}
	]
}
```

The purchase date is an ISO date (`YYYY-MM-DD`), quantities and costs are numbers, and receipt interactions are not stored by Gemini. When an item's name matches a stored alias, the API returns its canonical food item name with `aliasMatched: true`; unmatched names are returned unchanged with `aliasMatched: false`. Alias matching ignores letter case but distinguishes accents and requires a full-value match.

An unmatched receipt name can be corrected with `POST http://localhost:5044/api/food-items/aliases` using `originalName` (the Gemini text) and `canonicalName` (the corrected name). The API reuses an existing food item or creates it when needed, then stores the original name as an alias. Confirmed corrections do not store receipt quantities, dates, costs, or receipt history. The general health check is at http://localhost:5044/api/health.