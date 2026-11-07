# Plan 18: visual guidance from the displaced threshold

> **Superseded 2026-11-07.** The visual guidance overhaul, oasis1701/msfs-blind-assist#345, measures the glidepath from
> `NavigationCalculator.LandingThreshold`, the same landing threshold. When this branch took #345, `RunwayGlidepathOrigin`,
> `GlidepathAltitudeErrorFt`, `RunwayGlidepathOriginTests` and this plan's rule VG-20 were removed; VG-20 is now #345's
> frame-timing rule. The ILS cross-check below lives on as `VisualGuidanceDisplacedThresholdTests`, and the measured biases stand.
> What follows is the record as it was made.

## Part A (shared code)

`RunwayGlidepathOrigin.Of` and `VisualGuidanceManager`'s glidepath uses, rule VG-20, `RunwayGlidepathOriginTests` (10 pass): made on
the local branch `fix/visual-guidance-threshold` off `origin/main` (a4895b983) and, at the owner's word (2026-11-03: "you can do it
in the A350 branch"), cherry-picked onto this branch (2274cd9d5). Measured from the pavement start, which comes first on the
approach, a displaced threshold put the guidance's path offset × tan(angle) under a glideslope that crosses the landing threshold
(78 ft on OMDB 12L, 1493 ft displaced; 108 ft on a 2,055 ft offset); from the threshold it matches. Biases on `main`: only the PMDG
777's are measured (80/40, on a runway not recorded, consistent with a threshold without an offset); the rest are estimates, so
none changed (`docs/visual-guidance.md`).

## Task 6: the A350's bias from the threshold

A coupled ILS to VCBI 04 (threshold not displaced, elevation 27.2 ft) on 1.2.8, 2026-11-03, logged every second through
python-SimConnect (`PLANE ALTITUDE`, the distance to the runway end's own position):

| Distance (NM) | Height over the threshold (ft) | d × tan 3° (ft) | Bias (ft) |
| --- | --- | --- | --- |
| 4.99 | 1643.7 | 1587.4 | 56.3 |
| 4.48 | 1481.2 | 1427.5 | 53.7 |
| 3.98 | 1324.4 | 1269.0 | 55.4 |
| 3.50 | 1168.0 | 1114.5 | 53.5 |
| 3.01 | 1013.7 | 959.1 | 54.6 |
| 2.48 | 845.1 | 790.7 | 54.4 |
| 2.00 | 691.3 | 636.9 | 54.4 |
| 1.49 | 529.3 | 474.5 | 54.8 |
| 1.02 | 378.1 | 324.2 | 53.9 |
| 0.50 | 212.2 | 159.2 | 53.0 |

Median 54.4. With OMDB 12L's two approaches (2026-10-30, medians 52 and 53, also measured from its displaced threshold) the three
agree within 2 ft: `GlideslopeAltitudeBiasFt` 53 (was 52). `FlareAltitudeBiasFt` 15 is a height over the runway, untouched by a
threshold's offset.

## Still the owner's

- Task 1: the same comparison on a released aircraft (a displaced and a non-displaced threshold), for the shared fix's own record.
- Task 6 Step 2's displaced half on the A350 (OMDB 12L again, on the fixed guidance with the tone).
