# GT Cosmetic Catalog

BepInEx plugin for Gorilla Tag that displays the loaded cosmetic registry in an in-game catalog.

## Features

- Toggle the catalog with `F8`.
- Search and paginate available cosmetics.
- View cosmetic names, IDs, categories, and prices.
- Add an item to Gorilla Tag's normal shop cart.
- Start a purchase through Gorilla Tag's own purchase flow.

## Build

The project targets `.NET Framework 4.7.2` and references assemblies from the local Gorilla Tag installation.

```powershell
dotnet build .\GTCosmeticCatalog\GTCosmeticCatalog.csproj
```

Copy `GTCosmeticCatalog\bin\Debug\net472\GTCosmeticCatalog.dll` to the game's `BepInEx\plugins` folder.

The game and its server remain authoritative for ownership and purchases. Registry entries that are not available to the account or current store may be rejected.