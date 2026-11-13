# Alchemist's Arsenal

You run a small potion shop at the edge of the map. Every morning adventurers come to your counter, and you brew their bombs by hand. In the afternoon they carry those bombs down the road and fight with them, so what happens out there depends on what you made in the morning. A sloppy brew means weaker bombs, and fewer of them.

It's a 2D shop sim and auto-battler for Windows, made in Unity.

![Brewing at the cauldron](docs/readme/morning-cauldron.png)

## A day in the shop

### Morning

Each fighter in your party picks a job at the counter, and every job is a potion you make yourself. You toss leaves into the mortar and crush them with the pestle, stir the cauldron in circles to keep the heat in the band, then pour the brew and cork the flask. The benches run on 2D physics, so a careless pour spills and a hasty strike scatters the leaves. Later roads add malting: you steep the grain, let it sprout and dry it over a kiln.

### Afternoon

Your fighters set out without you. They pick their own targets, keep their distance and throw what you brewed, wave after wave. You watch, and you can speed the fight up or pause it.

### Evening

You're paid by how good your flasks were, and you keep whatever the road dropped. Spend it on more fighters, on kit for the road, or on machines that take some of the bench work off your hands. Then you sleep and pick tomorrow's road.

![A fight in the Whispering Woods](docs/readme/afternoon-woods.png)

## Where you'll go

There are five roads, each harder than the one before. Three of them:

- Whispering Woods, with Bark Treants and quick little Thornlings. It's your first job, and one fighter can clear it.
- Cinder Peaks, home to Emberlings and Cinder Hounds.
- Frostbite Caverns, full of Frostkin, and Rime Wraiths fast enough to run down a fighter who came alone.

Every monster has an element, and so does every flask. The right one hits twice as hard.

![The evening shop](docs/readme/evening-upgrades.png)

## Download and play

Get the latest build from the [Releases](../../releases) page.

### Windows

1. Download `AlchemistsArsenal-Windows.zip` and unzip it anywhere. Keep the files together: the `.exe` won't start without the folder next to it.
2. Run `AlchemistsArsenal.exe`.

The game isn't signed, so Windows may say it protected your PC. Click **More info**, then **Run anyway**.

### Linux

1. Download `AlchemistsArsenal-Linux.tar.gz` and unpack it: `tar -xzf AlchemistsArsenal-Linux.tar.gz`
2. Run `./AlchemistsArsenal/AlchemistsArsenal.x86_64`

You don't need to install anything else on either system.

## Controls

The game is played with the mouse.

| Input | What it does |
|---|---|
| Left click / drag | Work the benches, pick jobs, use the menus |
| Esc | Pause the morning, go back from the road map, skip a scene |
| Space / Enter | Next line in a scene |

## Minimum requirements

- Windows 10 or 11, 64-bit, with a DirectX 11 graphics card
- or Linux x86_64 (Ubuntu 22.04 or similar), with a graphics card that supports Vulkan or OpenGL 4.5

## Building from source

Open the project in Unity 6000.6 and build from **File > Build Profiles** (Windows x64, or Linux x64 with the Linux Build Support module).

## License

All rights reserved. You can download and play the game, but the code, art and music may not be reused without permission. See [LICENSE](LICENSE). The fonts (Gelasio, Selawik and Inconsolata) are used under the SIL Open Font License, with their licences in `Assets/Resources/Fonts`.
