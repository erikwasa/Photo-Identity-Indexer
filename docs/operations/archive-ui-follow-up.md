# Archive UI follow-up (WI-0054)

WI-0054 records three usability corrections found immediately after the real Windows/OneDrive acceptance of WI-0042 and WI-0041.

## Viewer behavior

Normal photo viewing remains non-hydrating. The viewer first serves the durable review proxy. If that proxy has not been generated yet, it may render a transient review-sized JPEG only when the authoritative original is already local and its bytes match the immutable revision hash. If the original is online-only, a normal viewer GET returns no preview and does not request hydration. Full-resolution hydration remains an explicit operator action.

## Progress labels

Archive summary counts are cumulative for the currently catalogued archive. The latest processing run is a separate durable batch and may contain fewer jobs than the cumulative analyzed count. The Archive page therefore labels its run counter `Latest run progress` rather than the ambiguous `Progress`.

## Availability reconciliation

Every explicit original status/view/hydrate/release operation that observes OneDrive availability records that observation in `archive_asset_availability`. Returning to Archive therefore reflects a recently observed transition such as `online-only -> downloading -> local -> online-only` without requiring `Advance archive` solely to refresh the persisted availability state.

This does not change managed-hydration ownership. If Photo Identity explicitly hydrated an online-only original, later archive advancement may still release that managed original once durable processing no longer needs it, subject to the accepted WI-0042 storage policy.
