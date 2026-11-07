# PantryPal

A minimal Angular frontend and ASP.NET Core API. MySQL integration is planned for a later step.

## Requirements

- Node.js and npm
- .NET 10 SDK

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

Open http://localhost:4200. The page checks the API at http://localhost:5044/api/health.