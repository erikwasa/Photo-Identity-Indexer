---
id: WI-0066
title: Add Smart Collection visibility preference for people
milestone: M19
status_source: ../status/work-items.yaml
depends_on: [WI-0050]
related_adrs: []
affected_modules: [PhotoIdentity.Persistence.Sqlite, PhotoIdentity.Api, PhotoIdentity.Web]
---

# WI-0066: Add Smart Collection visibility preference for people

## Objective

Allow a canonical person to be hidden from normal Smart Collection discovery without hiding, deleting or weakening that identity anywhere else in Photo Identity.

The preference is intentionally scoped to Smart Collections. A hidden person must remain fully available in face gallery/review, Face Details, person maintenance, assignments, suggestions, audits and other identity workflows.

## User contract

- Maintain People exposes a reversible `Hidden from Smart Collections` preference for each active person.
- Hidden people do not appear in the normal Smart Collection people picker/search and cannot be newly selected there.
- Hiding a person does **not** hide photos containing that person from Smart Collections built from tags, location, dates or other people.
- Hiding a person does not remove, rewrite or invalidate canonical face assignments, manual photo-level people or identity suggestions.
- Unhiding restores the person to normal Smart Collection discovery.

## Saved collection compatibility

A saved Smart Collection may already reference a person when that person is later hidden. That saved definition must remain valid and must not be silently rewritten.

When such a definition is reopened:

- the hidden person remains selected;
- the UI clearly marks that selected person as hidden;
- reevaluating the saved collection preserves the existing person criterion;
- removing the hidden person from the definition is allowed;
- after removal, the hidden person is not offered for re-selection until unhidden.

This makes the preference a discovery/presentation setting rather than a destructive query rule.

## Persistence and API contract

Persist explicit person-scoped presentation metadata rather than overloading identity state. The implementation may use a dedicated preference table or equivalent durable representation, but the semantic field should remain narrowly named, for example `HiddenFromSmartCollections`, rather than a generic `Hidden` flag.

The people response used by Smart Collections must expose enough information to distinguish visible and hidden people when restoring an existing saved definition. The ordinary picker should filter hidden people client-side or server-side without changing the canonical person list used by face-review workflows.

Merge behavior must be defined and tested. If a hidden person is merged into another person, the surviving person's visibility must follow an explicit deterministic rule; default to preserving the survivor's current preference unless implementation evidence justifies another rule.

## In scope

- Durable reversible person-level Smart Collection visibility preference.
- Maintain People hide/unhide control and status indicator.
- Smart Collection discovery filtering.
- Backward-compatible reopening of saved definitions that reference a now-hidden person.
- Automated persistence/API/UI behavior coverage.

## Out of scope

Global person hiding, deleting people, hiding photographs, suppressing face-review evidence, changing recognition/suggestion behavior, access control and per-collection visibility overrides.

## Acceptance criteria

- [ ] Maintain People can hide and unhide an active person from Smart Collections.
- [ ] The preference survives application restart.
- [ ] A hidden person remains visible and usable in face gallery/review, Face Details and person maintenance.
- [ ] A hidden person is absent from normal Smart Collection people discovery and cannot be newly selected.
- [ ] Hiding a person does not remove photos containing that person from tag/location/date/other-person Smart Collections.
- [ ] Existing saved Smart Collections that already reference a hidden person remain valid and reevaluate with the same person criterion.
- [ ] Reopening such a saved collection shows the hidden selected person with an explicit hidden indicator and allows removing them.
- [ ] Unhiding restores the person to normal Smart Collection discovery.
- [ ] Person merge behavior for the visibility preference is deterministic and covered by tests.
- [ ] No recognition models, embeddings, face assignments or suggestion evidence are rewritten by this preference.

## Suggested implementation slices

1. Persistence/API contract for the person visibility preference, including merge semantics and tests.
2. Maintain People hide/unhide control and status indicator.
3. Smart Collection discovery filtering plus saved-definition compatibility coverage.

## Implementation status

Slice 1 is in implementation on `agent/WI-0066-smart-collection-person-visibility`.

The foundation uses schema v16 with a dedicated `person_smart_collection_visibility` table and a sparse hidden-person preference. The maintenance API exposes a reversible visibility mutation plus the current flag while the ordinary review people endpoint continues to return every active person. Merge semantics are target-wins: the surviving person's existing preference is unchanged, and preferences attached to a retired source identity are excluded from active visibility resolution.

Integration coverage verifies restart persistence, reversible hide/unhide behavior, continued review-person availability, unknown-person rejection and target-wins merge semantics. Maintain People UI and Smart Collection picker behavior remain the next slices.
