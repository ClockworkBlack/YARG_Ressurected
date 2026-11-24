# Story mode: "Eleven Seconds"

A campaign for YARG Resurrected, played with the songs already in your library. It is an homage to
African American music: R&B, rap and pop, and the gospel, blues, jazz, soul, funk and go-go they grew
from.

> The most sampled four bars in history were cut in eleven seconds on Watch Night 1967, by five
> musicians whose names never reached the label. One of them is your grandmother, who has spent forty
> years tearing tickets at the Harlem theater where she should have been singing. You have a retired
> church van, a reel of tape, and until midnight on the next Watch Night to find the other four, win
> every room from the Bronx to Johannesburg, and get the roll called before the song is sold forever.

The story is told by the break itself. It is laid out like a double album: ten tracks on four sides,
and a hidden track after the credits.

- 11 chapters, 32 gigs, 94 songs to play, about 20,000 words of scenes.
- A gig at every one of the 26 venues, Harlem to Tokyo.
- 32 liner notes: short cards of real history, one unlocked at each gig.
- Every character, label and act in the story is made up. Real people, places and dates are named
  only in the liner notes.

## Playing

Open **STORY** at the top of the main menu. There are three save slots; a new story starts by picking a
band name.

1. **The tour** lists the chapters and their gigs. A gig opens when the one before it is won.
2. **A gig** is a set of one to five songs. The game suggests songs from your library that suit the
   night (by genre and year, when your songs carry them). Tap a song to swap it for any other song you
   have, or shuffle the whole set. Any song can be played at any gig.
3. Press **Play**. The usual instrument and difficulty screen appears once, then the songs play back to
   back with the usual score screen in between.
4. Each song gives up to five stars. Reach the gig's star target over the whole set to **win the
   crowd**. Falling short costs nothing: run it back.
5. Every gig also has an optional **challenge** (a streak, an accuracy mark, gold stars, bringing a
   friend...). Meeting it earns a badge and extra fans; on the last gig of each chapter it also unlocks
   an outtake from the night in 1967.

Stars earn **fans**, and fans raise the band's rank, from "Row Z" to "Legend". Along the way you
collect the six pieces of evidence the story turns on.

What counts toward a gig:

- Songs played to the end at full speed or faster, by at least one person. Computer-controlled players
  do not count, and neither do practice runs.
- With several people playing, the song's stars are the average of everyone's.
- Quitting part-way, or ending the setlist early, records nothing. The gig is simply still there.

You need songs in the library and at least one player set up (the gig screen says so, and has a
shortcut to the player list, if either is missing). Everything works by touch and by gamepad.

## Venues

Each gig belongs to a venue. When the venue feature of the `combined-updates` branch is in the build,
the gig is played on that venue's stage, and your own **Settings > General > Venue** choice is put
back as soon as the song has started. Two exceptions: if you set Venue to None, gigs respect that; and
a song with its own background or video keeps it. Without the venue feature, gigs use the normal
background.

## Saves

Progress is saved after every change, in the app's own folder, with a copy in
**Internal storage/YARG/story** so it survives uninstalling and reinstalling the app (which updating
often needs: see the README). The newer of the two copies is used. If the app is closed before a set
is finished, that set is dropped and the gig is still open.

## How it is built

Story mode is made only of new files. It adds no patch and changes no existing file, so it cannot
collide with other work on this repository:

- `overlay/Assets/Script/Story/Core/`: the rules, the save file, song suggestions and the script
  checker. Plain .NET with no Unity in it.
- `overlay/Assets/Script/Story/`: the part that talks to the game (starting a set, reading results from
  the score screen, the STORY row in the main menu) and the screens, all drawn in code.
- `overlay/Assets/Resources/YargStory/`: the story itself as JSON: `campaign.json`, `chapter01.json` to
  `chapter11.json`, `linernotes.json`. The writing can be edited there without touching code.
- `tests/StoryTests/` and `.github/workflows/story-tests.yml`: automated checks of the rules and of the
  script (every scene, venue, reward and challenge; that the story can be played from start to finish;
  that ranks are reachable). They run on every push that touches the story.

To check the script after editing it:

    dotnet run --project tests/StoryTests -- --check overlay/Assets/Resources/YargStory

## Dedication

For the session players, house bands and background singers whose names never reached the label.
