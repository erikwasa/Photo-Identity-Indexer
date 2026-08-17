# WI-0064 live-provider verification finding — 2026-08-17

The first maintainer live GeoNames run reached the configured provider but stopped after its first request:

- candidates: 5
- provider requests: 1
- cached results: 0
- assignments: 0
- deferred: 0
- failed: 1
- stopped early: yes

The existing Settings report exposed only aggregate counts and therefore hid the provider error code/reason even though the catalogue persisted `last_error_code` and `last_error_message`. The same run was also presented with a green "enrichment finished" banner despite the provider failure.

The corrective slice on `agent/WI-0064-provider-error-reporting` keeps raw provider details local while returning a sanitized stop reason to the operator. Known GeoNames authorization/limit/service conditions receive actionable guidance, and failed/deferred runs are no longer presented as successful completion.

The failed attempt remains retryable. Final WI-0064 acceptance still requires a successful maintainer live-provider sample after the provider/account condition is corrected and this reporting fix is merged.
