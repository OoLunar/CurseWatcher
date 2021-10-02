# CurseWatcher
## Watch any CurseForge project(s) from the comfort of Discord.
CurseWatcher polls the CurseForge API at whatever interval you specify, looking for any change in the `DefaultFileId` property. The `DefaultFileId` property is assumed to be the latest file, which changes every update. Once an update is found, it'll send an embed through a Discord webhook, providing a direct download link to the file! Simple and easy, just as all things should be.

## Setup
Copy [`config.json`](./res/config.json) to `config.json.prod`, and fill out the settings appropriately.

### Docker
Based off of Alpine.
```bash
docker run ghcr.io/oolunar/tomoe --mount ./config.json.prod,/src/res/config.json.prod
```

### Docker Compose
```yml
version: "3.9"

services:
  curse-watcher:
    image: ghcr.io/oolunar/curse-watcher:latest
    volumes:
      - ./config.json.prod:/src/res/config.json.prod
    restart: unless-stopped
```

### Dotnet
```bash
git clone https://github.com/OoLunar/CurseWatcher
cp res/config.json res/config.json.prod
read # Fill out config.json.prod
dotnet run
```

# Contributing
Make sure your editor/IDE respects the [`.editorconfig`](./.editorconfig) file. Requested for your code to be documented. Open a PR with your requested changes, make sure to clearly state what the PR would add and why it should be added.

# Copyright
All Copyright belongs to [OoLunar](https://github.com/OoLunar). Run at your own risk.