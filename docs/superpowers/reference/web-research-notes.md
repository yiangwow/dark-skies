# Web Research Notes — Alien Skies

Synthesized reference from Neon White / Ghostrunner / Ultrakill / DMC / CritPoints research + audio licensing + shader technique sources. One line of context per note; source kept for verification.

## Contents

- [Course Design Principles](#course-design-principles)
- [Style & Scoring](#style--scoring)
- [License-Clean Audio Options](#license-clean-audio-options)
- [Shader Techniques](#shader-techniques)
- [TOP 5 CHANGES TO MAKE](#top-5-changes-to-make)

---

## Course Design Principles

1. **Size for one-sitting mastery.** Neon White levels finish under ~30s at pace (Smackdown WR ~9.56s). Target: 20–45s gold-pace, 60s hard ceiling — at zip-dash 60m/s + pad +26m/s that's ~800–1500m of lane path, i.e. 5–8 segments of 5–8s. Tune so gold is plausibly reachable in ~15–20 min of retries. — [Game Informer](https://gameinformer.com/feature/2022/11/25/how-a-neon-white-level-is-made), [Shacknews](https://www.shacknews.com/article/131116/neon-white-review-down-with-the-quickness)

2. **Five generator invariants:** (1) next objective always known; (2) right tool always in hand for what's ahead; (3) ≥1 non-trivial shortcut per course; (4) shortcuts visible from the main path, never hidden; (5) shortcuts use only verbs granted on the main path. Validate rule 3 per generated course — no cross-cut → reject or mutate the layout. — [Game Developer](https://www.gamedeveloper.com/business/-neon-white-and-designing-for-player-creativity)

3. **One demand channel at a time.** Never stack traversal-execution, aim, and route-decision on the intended path. Combat pockets go on flat/low-speed ground between traversal segments. Stacked demands (shoot-while-swinging) are Ace-route and late-game only. — [Game Developer](https://www.gamedeveloper.com/business/-neon-white-and-designing-for-player-creativity)

4. **Dual-clarity playtest, twice per change:** blind pass (next objective obvious within one camera sweep?) and optimized pass (still flows at 60m/s?). Sightline budget: ~1.5–2s of look-ahead before each branch/landing = 90–170m at 60–86m/s. Cut anything ambiguous, even if it "feels important". — [Game Informer](https://gameinformer.com/feature/2022/11/25/how-a-neon-white-level-is-made)

5. **Physical color language per verb, baked into prefabs:** grapple anchors one emissive hue, launch pads a directional glow ramp along their vector, zip-dash targets a third color, combat pockets marked by arena lighting. Shortcut affordances get their verb's signature, slightly dimmer — legible, not arrowed. — [Game Developer](https://www.gamedeveloper.com/business/-neon-white-and-designing-for-player-creativity), [Game Informer](https://gameinformer.com/feature/2022/11/25/how-a-neon-white-level-is-made)

6. **Medals are named runs, not tuned numbers.** Bronze = finish; Silver = intended lane + N seconds slop; Gold = intended lane clean (every pad hit, no dropped swings); Ace = the designed cross-cut clean. Lanes are parametric → compute intended-path time analytically from segment lengths and verb speeds. — [Vice](https://www.vice.com/en/article/neon-whites-developers-being-too-good-at-their-own-game-caused-real-problems/)

7. **Ghost-degradation calibration.** Record input-replay ghosts of clean dev runs, then generate degraded variants in code (100–400ms hesitations at decision points, one dropped re-swing, split simultaneous inputs) and use those times as Silver/Gold. Automatable per generated course — hand-tuning won't scale. — [Vice](https://www.vice.com/en/article/neon-whites-developers-being-too-good-at-their-own-game-caused-real-problems/)

8. **Insight gating (UI state machine).** First entry: only "finish it." After finish: Silver/Gold targets + own ghost. After Gold: Ace target + shortcut hint (highlight the cross-cut anchor). Leaderboard never before Gold. — [Vice](https://www.vice.com/en/article/neon-whites-developers-being-too-good-at-their-own-game-caused-real-problems/)

9. **Restart under ~200ms, architecturally.** One-key restart + restart-on-death, no scene reload: pool and deterministically reset segment state (spawns, pad cooldowns, rope, projectiles, timer). Every segment implements `Reset()` from day one. Timer starts on first input, not spawn. — [Shacknews](https://www.shacknews.com/article/131116/neon-white-review-down-with-the-quickness), [Inverse](https://www.inverse.com/gaming/ghostrunner-interview-cyberpunk-hotline-miami), [GodisaGeek](https://godisageek.com/reviews/ghostrunner-review/)

10. **Checkpoints split by length.** Under ~60–90s: none on ranked runs — the course is the retry unit. Over ~90s: cut into two courses, or practice-mode-only section checkpoints (jump to segment N, timer invalidated) for drilling one gap or chain. — [GodisaGeek](https://godisageek.com/reviews/ghostrunner-review/), [CICADAMATA wiki](https://www.cicadamatawiki.wiki/)

11. **Three-course arc per verb:** intro (new verb is the only demand), combo (pair with one prior verb), playground (Ace route demands combining with quake air-control or rocket jump). Macro order: Movement Fundamentals → Final Execution. Creativity requires proficiency first. — [Game Developer](https://www.gamedeveloper.com/business/-neon-white-and-designing-for-player-creativity), [Game Informer](https://gameinformer.com/feature/2022/11/25/how-a-neon-white-level-is-made)

12. **Fold the geometry.** Generate loops, U-turns, stacked verticals — the "horseshoe" (start/end close, wall between) is the canonical skip-enabler. Discovered-skip test before patching: does a casual hit it by accident? / does it create interesting choice? No/yes → keep. Only fix skips that trivialize the teach. — [CritPoints](https://critpoints.net/2020/09/16/designing-for-speedrunners/)

13. **Iterate cheap, cull hard.** Generate 3–5 variants per course slot, timebox playtests, expect >50% cull — never polish a weak layout. Alternate high-execution segments with low-demand glide/landing segments; one off-route collectible per course reachable only by slow, deliberate verb use (pressure valve + route practice). — [Game Informer](https://gameinformer.com/feature/2022/11/25/how-a-neon-white-level-is-made), [Game Developer](https://www.gamedeveloper.com/business/-neon-white-and-designing-for-player-creativity)

---

## Style & Scoring

14. **Per-weapon freshness gates heat gain** (Ultrakill: FRESH 1.5x / USED 1.0x / STALE 0.5x / DULL 0.0x; attacking decays own weapon 0.7, regens others 0.5, holstered +0.1/s). Heat per hit = base × freshness; whiffs decay freshness but gain zero heat. Switching becomes optimal automatically. Show freshness as a tint on the weapon icon. — [Ultrakill wiki: Style](https://ultrakill.wiki.gg/wiki/Style)

15. **Make variety actions decay-free**, not just spam penalized. Add two zero-decay named bonuses: Quickdraw (damage within ~0.5s of swap) and Arsenal (kill with 2+ weapons contributing). Dedicated Voice of the Machine lines for both. — [Ultrakill wiki: Style](https://ultrakill.wiki.gg/wiki/Style)

16. **Clamp the freshness floor by loadout size:** 1-weapon tutorial course locks freshness at max, 2 weapons floors at 1.0x, full loadout floors at 0.5x. Never punish variety the course itself withheld. — [Ultrakill wiki: Style](https://ultrakill.wiki.gg/wiki/Style)

17. **Named bonuses price method, never volume.** Plain kill = small, near-silent feed entry; named entries only for HOW (airborne, ricochet, environmental, downbeat kill, kill-during-dodge). Named bonus pays 2–10x a plain kill, scaled by setup difficulty. Every environmental death gets a bespoke dry machine-humor name — exactly the Voice's register. — [Ultrakill wiki: Style](https://ultrakill.wiki.gg/wiki/Style)

18. **Heat tiers: steepening decay, one-tier damage drop, real rewards.** x4 drains several times faster than x2 (top tier is visited, not camped). Taking a hit drops exactly one tier — never full reset. Per-tier reward: audio stems/intensity layers unlock at x3–x4 + a small mechanical perk. Voice announces tier changes both directions. — [Ultrakill wiki: Style](https://ultrakill.wiki.gg/wiki/Style)

19. **The DMC invariant as a tuning test:** no single-weapon, single-verb loop may sustain x3+. Per-(weapon, kill-method) staleness counter — full value, ~50%, then ~0 (DmC's two-repeat rule) — plus idle decay so heat can't be banked. Playtest gate: a tester holding x4 with one gun = tuning failed. — [devilmaycry.org thread](https://devilmaycry.org/threads/dmc-3-4-style-meter-behavior.23616/)

20. **Movement amplifies heat.** Airborne / wall-run / course-verb states give heat gain a bonus multiplier that collapses (~0.2s) when grounded and static. Standing still and shooting must be the worst heat strategy, mechanically and audibly. — [Giant Bomb: Style Meter](https://www.giantbomb.com/style-meter/3015-1834/), [Ultrakill wiki: Style](https://ultrakill.wiki.gg/wiki/Style)

21. **Time and style stay orthogonal:** par-time medal + peak-heat grade as two separate stamps per course — a ladder each for speedrunners and stylists. And route the Insight system through the Voice: nothing on first clear, route tease at silver-tier, ghost at gold, the Voice flatly confirming the skip exists at top tier. — [Vice](https://www.vice.com/en/article/neon-whites-developers-being-too-good-at-their-own-game-caused-real-problems/), [Neon White wiki: Medals](https://neonwhite.fandom.com/wiki/Medals), [Game Developer](https://www.gamedeveloper.com/business/-neon-white-and-designing-for-player-creativity)

22. **Consider heat spendable:** cash one tier for a movement burst or music-synced special, so x4 has an opportunity cost (hold for multiplier + full audio, or spend for traversal) — kills camping from the other direction. And ship ~8–12 named bonuses, not 100; simple + versatile beats sprawl. — [Game Developer](https://www.gamedeveloper.com/business/-neon-white-and-designing-for-player-creativity), [Neon White wiki: Soul Card](https://neonwhite.fandom.com/wiki/Soul_Card)

23. **Quantize the combo window to the track's tempo:** chain kills within a bar (or N beats) to build an escalating xN feed counter — different course tracks produce different combo rhythms for free. Voice prosody escalates with the counter; a broken chain gets one dry acknowledgment. — [Ultrakill wiki: Style](https://ultrakill.wiki.gg/wiki/Style)

---

## License-Clean Audio Options

24. **Kenney.nl is the primary source: genuinely CC0**, verified on individual pack pages (each links the CC0 deed). 10 audio packs incl. Sci-fi Sounds (70), Interface Sounds (100), Impact Sounds (130), Digital Audio (60). Drop the unverified "Laser Weapons Sound Pack Free" entirely; re-source all 4 used clips from Kenney in one pass. Safe to commit raw files. — [kenney.nl/assets/category:Audio](https://kenney.nl/assets/category:Audio)

25. **Sci-fi Sounds maps 1:1 to current clips:** three distinct laser voices for rocket / energy rifle / shotgun (heavy `laserLarge` for rocket, mid laser for rifle, short retro laser for shotgun; pitch-shift per weapon for identity) + `explosionCrunch` for the explosion. OGG drops straight in; normalize gain to the current mix so pacing feel doesn't change. — [kenney.nl/assets/sci-fi-sounds](https://kenney.nl/assets/sci-fi-sounds), [OGA mirror](https://opengameart.org/content/sci-fi-sounds)

26. **Interface Sounds + Impact Sounds cover UI and physical layers** (menu nav, course select, countdown ticks; landings, wall scrapes) from the same vendor — whole SFX bill-of-materials auditable with a single LICENSE note in the audio folder. — [kenney.nl/assets/interface-sounds](https://kenney.nl/assets/interface-sounds), [kenney.nl/assets/impact-sounds](https://kenney.nl/assets/impact-sounds)

27. **Freesound for the speed whoosh/wind layer** (Kenney's one gap): CC0 filter is `f=license:"Creative Commons 0"` (416 CC0 "laser gun" / 451 CC0 "whoosh wind" hits). User-uploaded, unverified, variable loudness — prefer WAV ≥44.1kHz, loudness-normalize on import, and log sound ID + uploader + URL in the credits file (provenance is what makes CC0 auditable later). — [freesound.org search](https://freesound.org/search/?q=whoosh+wind&f=license%3A%22Creative+Commons+0%22), [FAQ](https://freesound.org/help/faq/)

28. **Sonniss GameAudioGDC: royalty-free but NOT CC0.** Commercial OK, no attribution, but redistribution as standalone files prohibited — raw WAVs in a public GitHub repo are a gray area. Use only if the repo stays private or files ship exclusively in cooked builds; document the constraint. Keep as the quality upgrade path, not the default. — [sonniss.com/gameaudiogdc](https://sonniss.com/gameaudiogdc)

29. **OpenGameArt advanced search** (Art Type = Sound Effect, License = CC0) as gap-filler when Kenney's lasers sound too arcade (e.g. a meatier rocket). Smaller pool, pre-curated for games; log URL + license at import. — [OGA search](https://opengameart.org/art-search-advanced?keys=laser&field_art_type_tid%5B%5D=13&field_art_licenses_tid%5B%5D=4)

30. **CC0 breakcore does not exist as a category** — the user's own tracks remain the soundtrack (settled). For menu/fallback only: 1–2 tracks from OGA's "CC0 Upbeat / Electronic Music" collection, tagged `fallback` so they never leak into course gameplay. Kenney Music Jingles for stingers. FreePD shut down 2025 — do not recommend it. — [OGA collection](https://opengameart.org/content/cc0-upbeat-electronic-music), [itch.io thread](https://itch.io/t/4614411/where-to-find-copyright-free-music-for-game-looking-for-drum-and-bassbreakcore-songs)

---

## Shader Techniques

31. **SpeedLines = polar remap + 1D noise**, not drawn lines: `angle = atan2(dy,dx)`, streak id = `hash(floor(angle*lineCount))`, animate via `radius - speed*time`. Rotate the pre-sample UV by the velocity angle to reorient the whole effect (side-rush lines) with zero new assets. — [RealtimeVFX thread](https://realtimevfx.com/t/anime-speedlines-background-shader/12452), [godotshaders radial-speedlines](https://godotshaders.com/shader/radial-speedlines/)

32. **Mask the center as an annulus:** multiply streak intensity by `smoothstep(innerRadius, outerRadius, r)`, jitter each streak's inner reach with its hash so the boundary is ragged. Gate on gameplay speed: boost intensity shrinks `innerRadius` so the effect swells during acceleration instead of popping. MirzaBeig's Unity URP implementation is Unlicense (free to reuse); Shadertoy equivalents are CC BY-NC-SA — technique only, never code. — [github.com/MirzaBeig/Anime-Speed-Lines](https://github.com/MirzaBeig/Anime-Speed-Lines)

33. **Datamosh = motion vectors + held previous frame + 16–32px block quantization.** URP has no GrabPass — the history frame must be a persistent RTHandle blitted in a Renderer Feature. Cheap first step for GlitchPass: block-quantized UV rounding alone reads as compression damage. Full feedback datamosh = stretch goal (extra fullscreen copy/frame). — [ompuco](https://ompuco.wordpress.com/2018/03/29/creating-your-own-datamosh-effect/)

34. **No-grab glitch architecture (correct for our overlay constraint):** block grid, hash-derived per-block displacement + tint, gated to time windows. Key digital feel = time quantization: seed with `floor(time*8..12)` so the pattern holds-then-snaps. Most blocks displace a few pixels, occasional hash-selected outlier — restraint keeps it from noise soup. One intensity scalar drives the mix. — [agatedragon pt.2](https://agatedragon.blog/2023/12/21/glitch-shader-effect-using-blocks-part-2/)

35. **RGB split as the final step:** red offset one way, blue the other, 2–6px (larger reads as stylization, not damage). Direction inherited from the local block's displacement vector so both read as one artifact; magnitude tied to the same intensity scalar. Split whatever the pass samples (`_BlitTexture` or the overlay's own layer). — [agatedragon RGB split](https://agatedragon.blog/2023/12/24/glitch-effect-with-rgb-split/)

36. **IQ domain warping is the highest-impact skybox upgrade:** `q = (fbm(p+o1), fbm(p+o2)); r = (fbm(p+4q+o3), fbm(p+4q+o4)); final = fbm(p+4r)`. Don't discard intermediates — `|q|`, `r.y` etc. are free channels for selecting 2–3 nebula colors. NeonPlanet gets gas-giant swirl from one warp level. Expose warp strength (the 4.0) as a material param. — [iquilezles.org/articles/warp](https://iquilezles.org/articles/warp/)

37. **Replace raw gain magic numbers with `gain = exp2(-H)`** — H∈[0,1] is a true roughness dial (1 = smooth terrain look, →0 = chaos). Audit octave counts: >~6 octaves in a skybox shader is GPU spent on sub-pixel detail; clamp by feature scale. — [iquilezles.org/articles/fbm](https://iquilezles.org/articles/fbm/)

38. **Analytical noise derivatives** come free from the interpolation coefficients: normals without 4–6 extra noise samples, and derivative-damped fbm (`scale each octave by 1/(1+dot(dsum,dsum))`) gives eroded, planet-like surfaces — smooth basins, rough coasts. — [iquilezles.org/articles/morenoise](https://iquilezles.org/articles/morenoise/)

39. **One ScreenFX uber-pass, not stacked features:** each Full Screen Pass costs ~two fullscreen copies, so merge SpeedLines + Glitch into one pass with `_SPEEDLINES_ON` / `_GLITCH_ON` shader_feature keywords — one screen sample, disabled effects compile out. Exception: SpeedLines alone is pure additive and never reads the screen — a transparent overlay quad after post is legitimately cheaper (zero blit). — [Unity discussions](https://discussions.unity.com/t/is-there-significant-overhead-to-using-multiple-full-screen-pass-renderer-features-in-urp/1494624), [Unity 6 docs](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html)

40. **Adopt the Blitter contract now** (foundations work, expensive to retrofit): `Blitter.BlitCameraTexture` + RTHandles instead of `cmd.Blit`; shader includes `Blit.hlsl` (use its fullscreen-triangle vertex + Varyings, delete your own) and reads `_BlitTexture` / `_BlitScaleBias`, not `_MainTex`. Getting it wrong = works in Game view, breaks in Scene view / XR / camera stacking. — [Unity URP blit guide](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@15.0/manual/renderer-features/how-to-fullscreen-blit.html), [Cyanilux](https://www.cyanilux.com/tutorials/custom-renderer-features/)

---

## TOP 5 CHANGES TO MAKE

Ranked by impact on current milestones (courses in design review, enemy-threat upcoming, audio license question open).

1. **Bake the five Neon White rules into the lane generator as hard invariants** (notes 2, 5): exit visible from entry, verbs granted before demanded, one plainly visible cross-cut per course using only granted verbs — reject/mutate layouts that fail, and bake the per-verb color signatures into the parametric prefabs. Courses are in review right now; this is the cheapest moment to encode it, and it converts the review checklist into automated validation.

2. **Adopt demand-channel separation as the spec for enemy-threat work** (note 3): combat pockets live on flat/low-speed ground between traversal segments; the intended path never requires aiming mid-swing or rocket-jumping under fire; stacked demands are Ace-route-only. Deciding this before enemy placement starts means the upcoming milestone inherits the constraint instead of fighting it — and note 4's 90–170m sightline budget applies to where threats become visible.

3. **Rebuild heat gain around freshness + movement** (notes 14, 15, 19, 20): heat per hit = base × per-weapon freshness (whiffs gain nothing), movement states amplify gain, Quickdraw/Arsenal bonuses decay-free, damage drops one tier not all. Ship with the DMC test as the tuning gate: no single-weapon loop sustains x3+. This fixes the project's own stated risk ("heat rises from merely firing; one weapon dominates") before combat content locks the tuning in.

4. **Do the Kenney re-source pass and close the license question** (notes 24–27): replace all 4 unverified clips from Sci-fi Sounds, pull UI/impact layers from Interface + Impact Sounds, get the whoosh/wind layer from Freesound's CC0 filter, and start the provenance credits file. Small, bounded task; ends the only open legal risk and makes the repo safe to publish.

5. **Mandate `Reset()` in the segment contract now, targeting sub-200ms restart** (note 9): pooled deterministic reset of spawns, pad cooldowns, rope state, projectiles, and timer — no scene reload — with the timer starting on first input. This is architecture, not polish: the segment system is in review, so the contract can still change for free; retrofitting after enemy state lands is the expensive path.

Runner-up once 1 and 5 land: the ghost-degradation medal calibration (note 7) — it depends on deterministic restart and stable lane generation, and it's the piece that makes per-generated-course thresholds scale without hand-tuning.