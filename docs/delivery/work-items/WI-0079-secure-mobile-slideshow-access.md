---
id: WI-0079
title: Add secure trusted-LAN phone access for slideshow use
milestone: M21
status_source: ../status/work-items.yaml
depends_on: [WI-0051, WI-0052]
related_adrs: []
affected_modules: [PhotoIdentity.Api, PhotoIdentity.Web, packaging, launcher, documentation]
---

# WI-0079: Add secure trusted-LAN phone access for slideshow use

## Objective

Provide a deliberate supported way to open Photo Identity from a phone on the trusted private network while preserving the current safe packaged default of loopback-only access.

The slideshow's primary phone use case cannot rely on the current packaged launcher alone: it only accepts loopback HTTP URLs. In addition, browser features used by M21 such as Screen Wake Lock and installable/service-worker PWA behavior require a secure context on normal non-loopback origins.

## Contract

- Default packaged behavior remains loopback-only and requires no network exposure change for operators who do not enable mobile access.
- Add an explicit opt-in trusted-LAN/mobile mode to the packaged launcher/host configuration.
- Mobile mode serves Photo Identity through HTTPS from an operator-selected address/hostname/interface rather than silently binding every interface.
- Certificate/key material and private host/address configuration remain outside the repository.
- The implementation accepts an operator-provided certificate suitable for the configured host and document how that certificate/issuing CA must be trusted by the phone.
- Firewall exposure is explicit and documented as trusted-private-network only.
- Do not introduce public hosting, internet exposure or cloud authentication as part of this item.
- Photo Identity remains unauthenticated under the current trust model; the UI/operations documentation must make the increased exposure clear.
- The secure mobile origin can load the Blazor application, API resources, manifest/service worker and slideshow image resources without mixed-content failures.
- The mobile page can prove `window.isSecureContext === true` on the supported path.
- Existing localhost/loopback launch, health checks, duplicate-instance detection and private launcher-settings behavior remain unchanged when mobile mode is disabled.

## Implementation notes

Prefer a small explicit launcher configuration surface rather than changing normal `url` validation to accept arbitrary remote addresses. Exact setting names may be chosen during implementation, but configuration must separate:

- the normal local browser URL;
- opt-in server listen address/host;
- HTTPS certificate location/credentials;
- optional advertised phone URL if it differs from the server listen endpoint.

Never log certificate passwords or other secrets. Paths/hostnames used by the operator are private configuration.

Because browser/device support varies, this work item establishes the **transport/access baseline** only. WI-0082 owns capability detection for fullscreen/orientation/wake APIs and their fallbacks.

## Acceptance criteria

- [ ] With no new mobile settings, packaged Photo Identity behaves exactly as today and only accepts/starts the loopback path.
- [ ] Enabling mobile access is an explicit operator choice; it is not inferred from network interfaces.
- [ ] Mobile access uses HTTPS and produces a secure browser context on a real phone after the operator trusts the configured certificate chain.
- [ ] The configured listener does not automatically widen to unrelated interfaces/addresses.
- [ ] A real phone on the trusted private network can open the Photo Identity UI and load normal same-origin API/image resources.
- [ ] Manifest/service-worker loading does not fail because the phone origin is insecure or mixed-content.
- [ ] Existing local health/startup checks continue to work in both normal and explicitly enabled mobile modes.
- [ ] Invalid/missing certificate configuration fails with an actionable, secret-free error rather than silently falling back to insecure remote HTTP.
- [ ] Launcher logs never print certificate passwords/private key content.
- [ ] Operations documentation explains the unauthenticated trusted-LAN risk, narrow firewall requirement, certificate trust requirement and how to verify the phone is using the intended secure origin.
- [ ] Automated tests cover default-loopback preservation, opt-in validation and invalid remote/TLS configuration.
- [ ] Maintainer verification confirms the secure origin on the real phone that will be used for M21 acceptance.

## Non-goals

- Internet/public hosting.
- User accounts or authentication.
- Automatic router/firewall configuration.
- Automatic public certificate issuance.
- VPN setup.
- Fullscreen, orientation lock, wake lock or toddler-protection behavior; those belong to later M21 work items.
