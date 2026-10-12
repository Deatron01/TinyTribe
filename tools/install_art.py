"""Copy the art the game uses out of the downloaded packs and into the Unity project.

The packs themselves are not in git. Download them once into Game/ArtSources/:

  Ninja Adventure - Asset Pack.zip   https://pixel-boy.itch.io/ninja-adventure-asset-pack   (CC0, by Pixel-Boy and AAA)
  kenney_roguelike-rpg-pack.zip      https://kenney.nl/assets/roguelike-rpg-pack             (CC0)
  kenney_emotes-pack.zip             https://kenney.nl/assets/emotes-pack                    (CC0)

Then run:  python tools/install_art.py

Only the files listed below are copied (the full packs are about 100 MB, mostly music).
Without them the game still runs, using simple pictures drawn in code.
"""
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCES = ROOT / "Game" / "ArtSources"
TARGET = ROOT / "Game" / "TinyTribe" / "Assets" / "Resources" / "Art"

NINJA_ZIP = "Ninja Adventure - Asset Pack.zip"
NINJA_PREFIX = "Ninja Adventure - Asset Pack/"

# Keep in step with PackArt.cs.
VILLAGERS = [
    "Villager", "Villager2", "Villager3", "Villager4", "Villager5", "Villager6",
    "Woman", "Boy", "OldMan", "Hunter", "Monk", "Cavegirl",
]
NINJA_FILES = {
    "Backgrounds/Tilesets/TilesetFloor.png": "TilesetFloor.png",
    "Backgrounds/Tilesets/TilesetNature.png": "TilesetNature.png",
    "Backgrounds/Tilesets/TilesetWater.png": "TilesetWater.png",
    "Backgrounds/Tilesets/TilesetRelief.png": "TilesetRelief.png",
    "Items/Object/Bag.png": "Bag.png",
    # Buildings, fire and animals. The pack has no wolf, deer or rabbit: a black dog, a horse and a hamster stand in.
    "Backgrounds/Tilesets/tileset_camp.png": "TilesetCamp.png",
    "Backgrounds/Tilesets/TilesetElement.png": "TilesetElement.png",
    "FX/Particle/Fire.png": "Fire.png",
    "Actor/Monster/Bear/SpriteSheet.png": "Animals/Bear.png",
    "Actor/Animal/DogBlack/SpriteSheet.png": "Animals/Wolf.png",
    "Actor/Animal/Horse/SpriteSheetBrownSide.png": "Animals/Deer.png",
    "Actor/Animal/Hamster/SpriteSheetBrown.png": "Animals/Rabbit.png",
    "LICENSE.txt": "LICENSE.txt",
}
for name in VILLAGERS:
    for sheet in ("Walk", "Idle", "Dead"):
        NINJA_FILES[f"Actor/Character/{name}/SeparateAnim/{sheet}.png"] = f"Characters/{name}_{sheet}.png"


def main() -> int:
    pack = SOURCES / NINJA_ZIP
    if not pack.exists():
        print(f"Missing {pack}\nDownload it from https://pixel-boy.itch.io/ninja-adventure-asset-pack (free, CC0).", file=sys.stderr)
        return 1
    out = TARGET / "NinjaAdventure"
    copied = 0
    with zipfile.ZipFile(pack) as z:
        names = set(z.namelist())
        for entry, destination in NINJA_FILES.items():
            source = NINJA_PREFIX + entry
            if source not in names:
                print(f"not in the pack: {entry}", file=sys.stderr)
                return 1
            path = out / destination
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(z.read(source))
            copied += 1
    print(f"Copied {copied} files to {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
