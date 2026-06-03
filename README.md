# Netclaw.Web

The Blazor management UI for Netclaw. This repo holds the web app on its own. It builds against the core Netclaw projects through a git submodule.

## Layout

- `src/Netclaw.Web/` — the Blazor server app.
- `src/Netclaw.Web.Tests/` — tests for the app.
- `netclaw/` — the core Netclaw repo, pinned as a submodule. The web app references `Netclaw.Configuration` from here.

## Getting the code

Clone with the submodule:

```bash
git clone --recurse-submodules git@github.com:codymullins/netclaw-ui.git
```

If you already cloned without it:

```bash
git submodule update --init --recursive
```

## Build and run

The app runs under .NET Aspire. The `aspire run` command starts the AppHost, which brings up the web app and its dependencies together:

```bash
aspire run
```

This needs the Aspire CLI. Install it once with:

```bash
dotnet tool install -g aspire.cli
```

To build, test, or run the web app on its own:

```bash
dotnet build NetclawUi.slnx
dotnet test NetclawUi.slnx
dotnet run --project src/Netclaw.Web
```

## The submodule

`netclaw/` points at the `codymullins/netclaw` fork on the `management-ui` branch. That branch carries the `Netclaw.Configuration` types the web app needs (for example `DaemonControlPlaneEndpointResolver`, `DiscordConfigWire`, and `ModelCatalogWire`). To move the pin to a newer commit:

```bash
cd netclaw
git fetch
git checkout <commit-or-branch>
cd ..
git add netclaw
git commit -m "Bump netclaw submodule"
```
