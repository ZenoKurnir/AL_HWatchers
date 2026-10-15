# AL_HWatchers

[日本語版はこちら (Japanese)](README.ja.md)

Other girls watch your H scenes in **AmanatsuLocation**. This is a BepInEx 6 (IL2CPP) plugin.

When H starts, girls from the current map gather around the couple and watch. As the scene goes on they react, get worked up, touch themselves, pair up with each other and join the bukkake at the end. Any watcher can also be swapped into the scene.

## Features

- **Watchers:** girls from the map come to watch, facing the scene. They keep clear of the people in it, walls and the camera. In H on a bed they stand on the floor around it.
- **Reactions:** surprised reactions, idle poses, faces and voices that follow how worked up each girl is.
- **Masturbation:** worked-up watchers touch themselves, standing, crouching or lying down. This is optional and needs Aicomi (see below).
- **Couples:** two watchers who are a linked couple in the game make love with each other (scissoring or 69) once both are worked up. They do it on the floor, away from walls, furniture and the camera.
- **Bukkake:** when he finishes over the girl, the watchers kneel beside her and get covered too.
- **Swapping girls:** a watcher can take the place of a girl in the scene. Her body, clothes, outfit, voice and tiredness carry over, and the game's clothes menu works for her. The girl she replaces becomes a watcher.
- **3P in solo H:** when H starts with a girl who has no partner, another girl from the map joins as the second girl, so 3P is available. Any watcher can be made the 3P girl.
- **Outfits:** in night H the watchers wear their after-bath clothes. Each girl keeps her own outfit across swaps.
- **Tired girls:** a girl who got worn out in H stays worn out when she is swapped out. She lies on the floor facing you and doesn't react, touch herself or join the others.
- **Girls with 0 hearts** only watch.

## Requirements

- AmanatsuLocation
- [BepInEx 6 (IL2CPP, bleeding edge)](https://builds.bepinex.dev/projects/bepinex_be), as included in the usual modpacks
- Optional: **Aicomi**, installed on the same PC, for the masturbation animations

## Installation

1. Download `AL_HWatchers-v1.0.7.zip` from the [Releases](../../releases) page.
2. Extract it into the game folder (the one with `AmanatsuLocation.exe`), so you end up with `BepInEx/plugins/AL_HWatchers.dll`.
3. Start the game.

## Controls

| Key | Action |
| --- | --- |
| F9 | Bring one more watcher |
| Shift + F9 | Send all watchers back |
| Ctrl + right-click a watcher | Open her menu |

The watcher menu shows her hearts, and lets you change her outfit and clothes and start or stop touching herself. You can also pick a partner and position for her, swap her into the scene, make her the 3P girl or send her back. While her menu is open, the game's move tool (bottom right of the H screen) moves her instead of the scene.

## Masturbation animations (Aicomi)

AmanatsuLocation has no masturbation animations. On first start, the plugin converts them from your own Aicomi install and saves the result in `BepInEx/cache/AL_HWatchers_aicomi.bin`. Nothing from Aicomi is included in this mod.

- Aicomi is found automatically in most cases. If it isn't, set its folder (the one that holds `lib`) in the config: **Watchers > Aicomi Folder**.
- Without Aicomi, everything else works; watchers just don't touch themselves.
- Delete the `.bin` file to convert again.

## Configuration

Edit `BepInEx/config/AL_HWatchers.cfg`, or use the ConfigurationManager plugin (F1).

| Setting | Default | What it does |
| --- | --- | --- |
| Bring Watchers Automatically | On | Girls come to watch when H starts |
| Maximum Watchers | 3 | How many girls can watch at once (1 to 5) |
| Distance (m) | 1.8 | How far from the couple they stand |
| Excitement Speed | 1 | How quickly they get worked up |
| Voice Volume | 0.4 | Watcher voice volume, relative to the game's |
| Masturbation | On | Worked-up watchers touch themselves |
| Aicomi Folder | (empty) | Where Aicomi is installed, if it isn't found |
| Couples Make Love | On | Linked couples make love with each other |
| Join the Bukkake | On | Watchers kneel and join the bukkake |
| Swap Girls | On | Watchers can be swapped into the scene |
| Second Girl in Solo H | On | Adds a second girl in solo H for 3P. Turn it off if H fails to start |
| Watcher Key | F9 | The key that brings watchers |
| Detailed Log | Off | Writes scene events to the BepInEx log, for bug reports |

## Notes

- The game still counts the H for the girl the scene started with, even after a swap.
- If two watchers switch partners in the middle of a 69, the pose can be slightly misaligned. Switching to scissoring and back fixes it.
- When reporting a bug, turn on **Detailed Log**, reproduce it, and attach `BepInEx/LogOutput.log`.

## Building

You need the .NET 6 SDK or newer, and a game folder where BepInEx has run once (so `BepInEx/interop` exists).

```
cd src
dotnet build -c Release -p:GameDir="C:\path\to\AmanatsuLocation\BepInEx"
```

The DLL is written to `src/bin/Release/net6.0/AL_HWatchers.dll`.

## License

MIT. See [LICENSE](LICENSE).
