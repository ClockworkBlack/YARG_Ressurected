# Desktop check: compile for every system in a minute, run the Linux build without Unity

The real test of the Windows, Mac and Linux versions is their GitHub workflow: Unity builds the
game and it is play-tested on a machine of its own system. That takes 10 to 30 minutes a round.
This script gives a first answer much sooner, on any Linux machine with the .NET 8 SDK:

```
bash tools/desktop-check/check.sh          # compile for Linux, Windows, Mac and Android
bash tools/desktop-check/check.sh --run    # ...and play-test the Linux build with this code
```

1. It puts the game's source together the way the builds do (YARG-DROID at the pinned commit,
   `patches/`, `patches-core/`, `overlay/`; no art assets).
2. It downloads the newest published Linux build, for the libraries the game is compiled
   against (Unity's, the plug-ins') and, with `--run`, for the game itself.
3. It compiles the game's own code (YARG.Core and everything under `Assets`) with the .NET
   SDK's C# compiler, once for each system's `#if` symbols. An error here is an error in the
   real build. Android is included because most of the code is shared with the phone version.
4. With `--run` it puts the freshly compiled Linux code into a copy of the Linux build and runs
   `tests/desktop-play-test.sh` on a virtual screen with a dummy sound card: the game starts, a
   phone joins over the network, Song Setup opens, and a bot plays the test song with the guitar
   sound generated and the microphone listening. Screenshots and the game's log are kept in
   `tools/desktop-check/work/play-test`.

`--run` also needs: `sudo apt-get install -y xvfb xdotool libgl1-mesa-dri pulseaudio libasound2-plugins libhidapi-hidraw0`

## What it cannot tell you

- It is not the Unity build. Scenes, prefabs and bundled resources (`Assets/Resources`, the
  phone page among them) come from the downloaded build, so changes to those are not seen here.
- Windows and Mac are compiled, not run. Their native libraries, sound and graphics are only
  exercised by their workflows.
- The Linux run uses software graphics, so it is slow (a few frames a second in a song). It
  shows that things work, not how fast.

Work files go in `tools/desktop-check/work` (not committed, about 1.5 GB). Delete the folder to
start afresh, for example after a new Linux build is published and you want its libraries.
