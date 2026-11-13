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

The frontend calls the API with relative `/api/...` URLs. During development, `npm start` forwards `/api` requests to `http://localhost:5044` using [frontend/proxy.conf.json](frontend/proxy.conf.json), so the API must be running on that port.

## Deploy

The API serves the built Angular app, so one process hosts both the UI (at `/`) and the API (at `/api`) on a single origin.

1. Build the frontend. The production build is written to `backend/wwwroot/` (git-ignored):

   ```sh
   cd frontend
   npm run build
   ```

2. Publish the backend from the repository root. The published output includes `wwwroot`:

   ```sh
   dotnet publish backend/backend.csproj -c Release
   ```

3. Run the published app on the host. Unknown non-API paths fall back to `index.html` so client-side routes work.

On the host, provide the MySQL connection string (`ConnectionStrings__PantryPal`) and Gemini key (`Gemini__ApiKey`) as environment variables or the host's secret store instead of `appsettings.json`, and serve over HTTPS. The development CORS policy for `http://localhost:4200` is not needed when the app is served from one origin.

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

Each displayed line is saved separately, even when multiple lines reference the same food. Repeating an identical hash and ordered item list is idempotent; reusing a hash with different purchase data returns `409`. Re-uploading the same exact image file is treated as the same receipt; different image bytes produce a different hash. The general health check is at http://localhost:5044/api/health.

## Pantry item tags

Create the following tables manually in MySQL before running the tag-enabled backend. They use the existing `food_items.id` type (`BIGINT UNSIGNED`), InnoDB, and a case-insensitive, accent-sensitive tag-name collation. Tags are shared by multiple food items; a tag remains available only while it is assigned to at least one food item.

```sql
CREATE TABLE food_tags (
	id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
	name VARCHAR(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_ci NOT NULL,
	PRIMARY KEY (id),
	CONSTRAINT uq_food_tags_name UNIQUE (name)
) ENGINE=InnoDB;

CREATE TABLE food_item_tags (
	food_item_id BIGINT UNSIGNED NOT NULL,
	food_tag_id BIGINT UNSIGNED NOT NULL,
	PRIMARY KEY (food_item_id, food_tag_id),
	INDEX ix_food_item_tags_food_tag_id (food_tag_id),
	CONSTRAINT fk_food_item_tags_food_items
		FOREIGN KEY (food_item_id) REFERENCES food_items (id) ON DELETE CASCADE,
	CONSTRAINT fk_food_item_tags_food_tags
		FOREIGN KEY (food_tag_id) REFERENCES food_tags (id) ON DELETE CASCADE
) ENGINE=InnoDB;
```

`GET http://localhost:5044/api/tags` lists tags currently assigned to food items. `PUT http://localhost:5044/api/food-items/{foodItemId}/tags` replaces that food item's full tag set; for example, send `{ "tags": ["Drinks", "Pop"] }`. Send an empty array to remove all tags from an item. Tag names are trimmed, limited to 100 characters, and deduplicated case-insensitively. Tags left with no food-item associations are deleted. The Pantry Items view displays and edits tags without changing purchase quantities or spending totals.