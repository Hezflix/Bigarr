# Bigarr

Gestion unifiée de médiathèque : une seule application pour remplacer, brique par brique, Radarr puis Sonarr, Seerr, Tautulli et Prowlarr.

Ce que Bigarr corrige au niveau du modèle : un film peut avoir **plusieurs versions** dans la même instance (1080p, 4K, Director's Cut…), chacune monitorée, cherchée et upgradée indépendamment. En sortie, Bigarr expose une API compatible **Radarr v3** pour que Seerr, Bazarr et le reste de l'écosystème s'y branchent sans le savoir.

- Statut : **MVP en cours**, jalon M0 (squelette). Films uniquement.

## Stack

C# / .NET 10, ASP.NET Core minimal APIs, EF Core + SQLite, React + Vite + TanStack, Docker multi-arch. Licence GPL-3.0.

## Développement

```bash
# backend
dotnet build
dotnet test

# front (servi en statique par le backend une fois buildé)
cd web && pnpm install && pnpm dev
```

## Licence

GPL-3.0-or-later. Le parseur de releases est vendoré depuis [Radarr](https://github.com/Radarr/Radarr) (GPL-3.0), voir `src/Bigarr.Parsing/VENDORED.md`.
