# Audio Provenance — CC0 Replacement Pack

This file is the license record for every audio clip in this directory. It
replaces the previously-used, unverified "Laser Weapons Sound Pack Free"
(`Assets/Laser Weapons Sound Pack/`), which shipped with no license file and
no verifiable source. That pack is REMOVED from the repo in the integration commit (this file is the standing license record) but
is no longer the source for any new wiring — see `docs/superpowers/reference/
web-research-notes.md` § "License-Clean Audio Options" (notes 24–27) for the
research that identified these replacements.

Sourced and verified 2026-08-29.

---

## rocket_fire.ogg

- **Source pack:** Kenney — Sci-fi Sounds (v1.0)
- **Original filename:** `Audio/laserLarge_000.ogg`
- **Pack page:** https://kenney.nl/assets/sci-fi-sounds
- **Exact download URL:** https://kenney.nl/media/pages/assets/sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip
- **License:** Creative Commons CC0 1.0 — https://creativecommons.org/publicdomain/zero/1.0/
  - Quoted from the pack's own `License.txt`: "License: (Creative Commons Zero, CC0) ... This content is free to use in personal, educational and commercial projects. Support us by crediting Kenney or www.kenney.nl (this is not mandatory)"
  - Quoted from the Kenney pack page's License table row: "License — Creative Commons CC0" (linking to the CC0 deed above)
- **Measured duration:** 0.68s (ffprobe, this session)
- **Rationale:** heaviest/longest of the pack's three laser voices — matches "rocket launch" per web-research-notes note 25 ("heavy `laserLarge` for rocket").
- **Date verified/downloaded:** 2026-08-29

## rifle_fire.ogg

- **Source pack:** Kenney — Sci-fi Sounds (v1.0)
- **Original filename:** `Audio/laserSmall_000.ogg`
- **Pack page:** https://kenney.nl/assets/sci-fi-sounds
- **Exact download URL:** https://kenney.nl/media/pages/assets/sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip
- **License:** Creative Commons CC0 1.0 — https://creativecommons.org/publicdomain/zero/1.0/ (same pack, same `License.txt` quote as above)
- **Measured duration:** 0.24s (ffprobe, this session)
- **Rationale:** shortest, snappiest laser voice — mid-weight single-shot fits "energy rifle" per note 25 ("mid laser for rifle"), by elimination against the large/retro voices used for rocket/shotgun.
- **Date verified/downloaded:** 2026-08-29

## shotgun_fire.ogg

- **Source pack:** Kenney — Sci-fi Sounds (v1.0)
- **Original filename:** `Audio/laserRetro_000.ogg`
- **Pack page:** https://kenney.nl/assets/sci-fi-sounds
- **Exact download URL:** https://kenney.nl/media/pages/assets/sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip
- **License:** Creative Commons CC0 1.0 — https://creativecommons.org/publicdomain/zero/1.0/ (same pack, same `License.txt` quote as above)
- **Measured duration:** 0.24s (ffprobe, this session)
- **Rationale:** short, punchy, retro-voiced zap — matches "shotgun-ish blast" per note 25 ("short retro laser for shotgun").
- **Date verified/downloaded:** 2026-08-29

## explosion.ogg

- **Source pack:** Kenney — Sci-fi Sounds (v1.0)
- **Original filename:** `Audio/explosionCrunch_001.ogg`
- **Pack page:** https://kenney.nl/assets/sci-fi-sounds
- **Exact download URL:** https://kenney.nl/media/pages/assets/sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip
- **License:** Creative Commons CC0 1.0 — https://creativecommons.org/publicdomain/zero/1.0/ (same pack, same `License.txt` quote as above)
- **Measured duration:** 1.36s (ffprobe, this session) — chosen over `explosionCrunch_000.ogg` (0.78s) for a heavier, more impactful hit.
- **Rationale:** dedicated explosion SFX in the same pack as the three laser voices, per note 25 ("`explosionCrunch` for the explosion").
- **Date verified/downloaded:** 2026-08-29

## wind_loop.mp3 (optional — speed whoosh)

- **Source:** Freesound.org, sound #60013, "Whoosh", uploaded by user **qubodup**
- **Sound page:** https://freesound.org/people/qubodup/sounds/60013/
- **Exact download URL used:** https://cdn.freesound.org/previews/60/60013_71257-hq.mp3 (Freesound's public "HQ preview" MP3, 128kbps/44.1kHz stereo)
- **License:** Creative Commons 0 — https://creativecommons.org/publicdomain/zero/1.0/
  - Quoted directly from the sound page's license link: "Creative Commons 0" (linking to the CC0 1.0 deed above)
- **Measured duration:** 0.47s (ffprobe, this session)
- **Format note / deviation:** per web-research-notes note 27, a WAV ≥44.1kHz original is preferred. Freesound only serves original uploader files (WAV) to authenticated API/OAuth clients; no such credentials were available in this session. The file above is Freesound's own public HQ preview MP3 (same CC0-licensed sound, transcoded, no auth required) — served at 44.1kHz. If a WAV original is later obtained through an authenticated Freesound account, swap this file in-place and update this entry; the sound ID and license do not change.
- **Loop note:** this is a single whoosh transient, not a seamlessly-loopable wind bed — only 15 CC0 hits were reviewed for `"whoosh wind"` and this was the cleanest, most clearly-CC0-labeled single-file match found in that set. Treat as a one-shot "speed whoosh" cue, not a loop, unless re-auditioned.
- **Date verified/downloaded:** 2026-08-29

---

## Verification method (this session)

1. Fetched each source page's raw HTML with `curl` (not rendered) and grepped
   for the literal license text/link and the real download URL — for Kenney,
   the `href` behind the page's "Continue without donating..." link inside
   the `#inline-download` popup; for Freesound, the CC0 deed link and the
   `cdn.freesound.org/previews/...` URL on the sound's own page.
2. Downloaded each file directly (`curl`), unzipped the Kenney pack, and
   confirmed its bundled `License.txt` independently states CC0.
3. Measured every clip's duration with `ffprobe` (see per-file entries above)
   to pick which same-pack variant best matches each weapon's role — no
   duration or filesize claim above is guessed.
