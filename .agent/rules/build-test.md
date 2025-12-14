---
trigger: always_on
---

example working to see build error: dotnet build .\OpenBullet2.Native\OpenBullet2.Native.csproj /clp:NoSummary 2>&1 | Select-String -Pattern "error"