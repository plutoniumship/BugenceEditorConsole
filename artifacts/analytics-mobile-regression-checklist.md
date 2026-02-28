# Analytics Mobile Regression Checklist

Use this checklist on:
- `1440x900`
- `834x1112`
- `390x844`
- `320x640`

Test route:
- `/Analytics/Index`

Login seed account:
- `admin@bugence.com`
- `Bugence!2025`

## Header
- Domain selector is visible and usable.
- Range pills wrap or scroll without clipping.
- Export button remains reachable.
- Compare toggle, segment, filter, and saved-view actions do not overflow off-screen.

## Tabs
- Tab strip remains reachable on small screens.
- Active tab state is still visually clear.
- Switching tabs does not cause horizontal page overflow.

## Tables
- Search input and toolbar controls stack correctly on mobile.
- Tables scroll horizontally inside their card instead of pushing the whole page wider.
- Pagination remains usable at `320px`.
- Column picker opens without clipping outside viewport.

## Drawers
- Filter drawer fully covers mobile width and remains scrollable.
- Saved views drawer fully covers mobile width and remains scrollable.

## Tab-specific checks
- Realtime geo map fits card width.
- Acquisition module tabs can be reached on mobile.
- Audience dimension tabs can be reached on mobile.
- Conversions funnel controls remain usable.
- SEO connector controls remain usable.

## Regressions to confirm
- No horizontal body scroll.
- No overlapping hero controls.
- No clipped buttons at bottom of cards.
- No broken layout when switching between tabs with saved views applied.
