# Flux Local Validation

Use this command set before merging changes that affect `Flux.sln`.

```powershell
dotnet restore Flux.sln
dotnet build Flux.sln -c Debug --no-restore

dotnet test RuriLib.Tests/RuriLib.Tests.csproj
dotnet test RuriLib.Http.Tests/RuriLib.Http.Tests.csproj
dotnet test Flux.Shared.Tests/Flux.Shared.Tests.csproj

dotnet list Flux.sln package --vulnerable --include-transitive
```

For EF Core migration changes, also verify the migration is discoverable:

```powershell
dotnet ef migrations list --project Flux.Core/Flux.Core.csproj --startup-project Flux.Web/Flux.Web.csproj --no-build
```

For frontend payload or UI build changes, also run:

```powershell
cd flux-web-client
npm ci
npm run build
```
