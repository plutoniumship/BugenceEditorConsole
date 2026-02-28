# Analytics Final QA Matrix

This matrix covers the remaining live-environment verification for the Analytics page after code-level implementation and automated service/page-model coverage.

## Environment limits

- Browser automation could not be executed in this workspace because `node` and `npm` are not installed.
- These checks should be run against a real signed-in browser session with production-like analytics data.

## Coverage already automated

- Analytics service tab logic
- Audience city/browser dimension output
- Conversion compare-period values
- Analytics Razor page `OnGetTabAsync` normalization and eligibility checks
- SEO connector status endpoint contract

## Manual live-data checks

1. Overview
- Verify KPI cards match exported CSV totals for the same range.
- Verify `Top Pages`, `Top Referrers`, and `Top Countries` match visible session volume.
- Verify blood-red progress bars render consistently and stay aligned at all row counts.

2. Realtime
- Open page in two browser sessions and confirm `Active users right now` rises.
- Trigger page views and key events and confirm live stream updates.
- Confirm empty realtime state does not show fake non-zero values.

3. Acquisition
- Verify `source / medium` rows match current attribution logic.
- Confirm sort, pagination, search, and column toggles persist visually.
- Confirm totals in compare mode match the expected previous period.

4. Engagement
- Verify page rows line up across all numeric columns.
- Confirm sorting by `views`, `active users`, `event count`, and `key events`.
- Confirm empty engagement-time data shows clearly as untracked rather than misleading.

5. Audience
- Verify `country`, `city`, `device`, and `browser` toggles all return data for the same range.
- Confirm country labels render as names, not raw unknown codes unless data is missing.
- Confirm city/browser rows with missing data collapse into sensible fallback labels.

6. Conversions
- Verify current-period key event totals equal the tracked event rows.
- Verify previous-period compare values change correctly when range changes.
- Confirm conversion journey steps reflect real tracked paths and do not show fabricated counts.

7. SEO / Search Console
- Verify disconnected state.
- Verify connected/no-cache state.
- Verify connected/cached state after a real sync.
- Confirm property selection persists and the latest snapshot renders.

8. Pages
- Verify full pages table alignment with long URLs.
- Confirm pagination and sorting remain stable across page changes.
- Confirm row counts match the engagement table for the same dataset where expected.

## Responsive checks

1. Desktop
- 1440px width
- 1280px width

2. Tablet
- 1024px width
- 768px width

3. Mobile
- 430px width
- 390px width

For each width:
- no page-level horizontal overflow
- tab strip scrolls cleanly
- table containers scroll inside cards
- controls remain clickable
- header remains readable

## Data-validation spot checks

1. Compare current KPI values against:
- exported CSV
- raw `AnalyticsPageViews`
- raw `AnalyticsEvents`
- raw `AnalyticsSessions`

2. Validate these dimensions directly in the database for one project:
- `Channel`
- `City`
- `Browser`
- `LandingPath`
- `UtmSource`
- `UtmMedium`

3. Confirm there are no modules showing non-zero fake placeholders when backing data is absent.

## Completion criteria

- All automated tests pass.
- Desktop/tablet/mobile manual checks pass.
- KPI, table totals, and compare values reconcile with raw analytics rows.
- SEO connector states are verified in a real environment.
