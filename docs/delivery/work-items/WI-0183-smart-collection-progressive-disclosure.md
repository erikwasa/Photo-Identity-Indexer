---
id: WI-0183
title: Simplify Smart Collection editor with progressive disclosure
milestone: M32
status_source: PhotoIdentity.Docs
depends_on: []
related_adrs: []
affected_modules: [PhotoIdentity.Web, PhotoIdentity.Integration.Tests, docs]
---

# WI-0183: Simplify Smart Collection editor with progressive disclosure

## Context

Issue #491 records that `/smart-collections` has become too long as optional filters accumulated. GPS rectangle, age-at-photo and family-relationship details are currently rendered even when their enable checkbox is off, usually in a disabled state. Generic Tags authoring is also present even though it is not used in the maintained workflow.

The goal is to reduce scanning/scrolling cost without changing Smart Collection membership semantics.

## Scope

- Render GPS coordinate fields only when `Also limit to GPS rectangle` is enabled.
- Render age-at-photo person/minimum/maximum controls only when the age criterion is enabled.
- Render family-relationship person/kind controls only when the relationship criterion is enabled.
- Keep enable/disable state understandable and accessible when optional details appear/disappear.
- Remove the generic Tags authoring section from the normal Smart Collection editor.
- Preserve existing underlying tag contracts/persistence/query capability; this UI cleanup must not silently remove a persisted filter capability.
- When reopening an existing Smart Collection that already contains generic tag criteria, preserve those hidden criteria on save rather than clearing them accidentally. Surface a concise legacy/hidden-filter notice if necessary to prevent misleading edits.
- Keep phone/PWA layout compact and free of horizontal overflow.

## Product constraints

Progressive disclosure should reduce page length without turning optional filters into modal dialogs or separate pages. The checkbox/summary that activates a criterion must remain visible enough that users can discover the capability.

## Out of scope

- Removing Tags from Core/API/PostgreSQL contracts.
- Changing `any`/`all` semantics for people/tags.
- Redesigning Smart Collection result browsing or Creative Collection recipes.

## Acceptance criteria

- [x] GPS South/West/North/East inputs are absent from the rendered form until GPS rectangle filtering is enabled.
- [x] Age-at-photo detail controls are absent until the age criterion is enabled.
- [x] Family-relationship detail controls are absent until the relationship criterion is enabled.
- [x] Generic Tags authoring is no longer shown in the normal new/edit Smart Collection UI.
- [ ] Existing definitions containing tag criteria can be opened and saved without silently dropping their tags; automated coverage confirms the request/navigation state remains tag-aware, while a representative maintainer save is still required.
- [ ] Enabling/disabling optional criteria remains keyboard and screen-reader accessible and restores/clears editor state according to existing semantics; native checkbox semantics are retained, with maintainer interaction verification still required.
- [ ] The editor is materially shorter in its default state and remains usable on desktop and phone/PWA without horizontal overflow; visual verification remains required.
- [x] Focused Web/navigation-state coverage is present; maintainer desktop/phone verification remains required.

## Implementation notes

PR #496 removes the generic Tags fieldset from the normal authoring surface while deliberately retaining `SelectedTags` and `TagMatch` in saved-definition requests, transient query requests and browser-tab navigation state. Saved collection cards show a tag summary only when tag criteria actually exist, and opening such a collection displays a concise notice explaining that the hidden criteria will be preserved on save or preview.

The GPS coordinate grid now renders only while `UseLocation` is enabled. Age person/minimum/maximum controls render only while `UseAgeFilter` is enabled, and family person/relationship-kind controls render only while `UseRelationshipFilter` is enabled. Turning a criterion off continues to omit it from the request through the existing request-building logic; turning it back on restores the current editor values, preserving established semantics.

`SmartCollectionProgressiveDisclosureTests` protects the conditional-rendering source contract, verifies the normal Tags authoring UI stays absent, confirms saved/query/navigation paths continue carrying hidden tag criteria, and exercises JSON round-tripping of tag values and match mode through `SmartCollectionTransientNavigationState`.

## Verification status

Implementation is in review in PR #496. CI plus maintainer desktop/phone verification remain outstanding. In addition to the compact-layout checks, maintainer verification should open a representative pre-existing Smart Collection containing tag criteria, confirm the preservation notice is visible, save it, and confirm its membership/tag criteria remain unchanged.
