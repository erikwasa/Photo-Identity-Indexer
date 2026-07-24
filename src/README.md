# Source projects

The solution is a modular monolith. Infrastructure projects depend on `PhotoIdentity.Core`; Core does not depend on infrastructure.

Executable composition roots:

- `PhotoIdentity.Cli`
- `PhotoIdentity.Worker`
- `PhotoIdentity.Api`
- `PhotoIdentity.Web`

Infrastructure adapters:

- `PhotoIdentity.Persistence.Sqlite`
- `PhotoIdentity.Source.Local`
- `PhotoIdentity.Source.OneDriveSync`
- `PhotoIdentity.Imaging.OpenCv`
- `PhotoIdentity.Recognition.Onnx`
- `PhotoIdentity.Transfer.Bundles`
