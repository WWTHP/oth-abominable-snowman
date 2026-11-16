# oth-abominable-snowman

**AbominableSnowman**: an [Over the Hill](docs/games/over-the-hill/README.md) MelonLoader mod.
A yeti paces across snowy challenge trails. Time your run past him, or he grabs your car and
hurls it a couple of hundred feet in a random direction. Synced with every player in the session
who has the mod. **F6** opens the menu.

```bash
python3 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
tools/check.sh                                   # all checks, no deploy
cd AbominableSnowman && dotnet build -c Release  # build + deploy to the game
```

Details: [docs/project/README.md](docs/project/README.md). Agents: start at [AGENTS.md](AGENTS.md).
