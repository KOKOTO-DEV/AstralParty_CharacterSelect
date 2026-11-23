# Astral Party Character Select 1.2.0

This BepInEx 6 IL2CPP plugin adds character tags, sorting, random picks, and team recommendations to the international Steam edition.

Default seats and roles are applied once on the first co-op pick screen with a new configuration. This requires an owned co-op level 6 character. Komachi, Fen, and Megas also require confirmed potential awakening. Existing settings and manual edits are retained.

## Installation and settings

Launch the game once with BepInEx, exit, then merge the ZIP's BepInEx folder into the game directory. Keep one copy of the plugin DLL. Edit BepInEx/config/astral-party.character-select.cfg with the game closed. Set Language to Auto, Korean, English, Japanese, SimplifiedChinese, or TraditionalChinese. Auto checks the displayed game text, including Korean patches. Configuration keys are stable and comments are in Korean. Custom labels retain the wording you enter.

Set character tags and edit the 51 default combinations in character details. Edit the 10 map presets from the map encyclopedia. Sort cycles through Default, Seat, Role, Custom, and Manual. CustomOrder accepts tag keys or names (for example P1, Card, P2, Support). ManualOrder accepts character IDs. Empty or invalid custom and manual modes are skipped. Tag and Random buttons cycle through unpicked characters first, then attempt the remaining characters. Final confirmation uses the game's button.

## Team Pick

- Candidates must have a tag and be eligible for selection. Map exclusions apply. A candidate must match an enabled combination with at least one teammate or appear in the map's Preferred list. A fixed-seat combination requires the matching seat tag.
- Score one point per matched combination, one point for being on the map's Preferred list, and one point for matching your current seat tag. Each combination counts once. Characters teammates have not selected come first. Within each group, sort by total points, then matched-combination count, then map priority, then combination order.
- Press Team Pick to calculate. Each further click cycles through the ranks while the team is unchanged. A teammate changing character never automatically replaces your pick. The next click starts with a fresh ranking.
- Emergency Pick is off by default. Open character details → Global Settings → Emergency Pick to enable it, choose a target of 1–4 players per role, and toggle Dealer, Support, Card, and Tank. Support is selected initially. Use Hero Settings to include or exclude that character as a candidate. On crossing five seconds, the plugin checks once and picks an eligible character that best reduces the combined role shortage. It retains an existing manual pick made without Team Pick.
- Set RecommendationDiagnostics to true to log team and candidate character IDs when clicking Team Pick. It defaults to false.
