# Azure tenant and identity constraints

The photo library is in personal OneDrive. Azure access is governed by an enterprise tenant that restricts app registrations and application identities.

The design must not require:

- Microsoft Entra app registrations
- Service principals
- Managed or workload identities
- Federated credentials
- Client secrets or certificates
- Microsoft Graph permissions in the enterprise tenant
- An Azure-hosted process authenticating to personal OneDrive

The Windows computer is the trusted bridge. Azure receives only explicit processing bundles and returns results. Interactive Azure CLI, PowerShell, Portal, Storage Explorer or AzCopy access may be used by the user from the local computer.
