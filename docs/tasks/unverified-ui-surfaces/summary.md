# Heroic store sub-chips: check on real data

Seeded 2026-09-08 from the v0.5.4 backlog entry ("v0.5.4 surfaces without hardware coverage"). The second surface it
named, gamepad navigation of Game Mode's Add Games filter pills, was confirmed on a real Deck by the maintainer on
2026-10-04.

What is left: in the agent UI's Add Games, choosing the **Heroic** filter shows a second row of store chips (Epic /
GOG / Amazon). It has never been seen on real data: the test Deck has no Heroic games, so the Heroic chip itself
(correctly) never appeared. It cannot lose save data; the worst case is a list that filters oddly. Code:
`agent-ui/src/components/AddGamesView.tsx` (the store axis, around lines 51 and 212-225, the row at 297).

## How

Install Heroic on the Deck (or a Windows PC), install one small free Epic or GOG game, open Add Games in the agent UI,
choose Heroic, and check the store row shows the right store and count and narrows the list.

## Done when

The store row is seen working, or its bug is fixed.
