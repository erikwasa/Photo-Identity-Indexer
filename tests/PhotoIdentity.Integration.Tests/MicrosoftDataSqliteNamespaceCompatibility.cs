// Transitional source-only namespace marker. The active integration tests execute entirely
// against PostgreSQL; remaining historical `using Microsoft.Data.Sqlite` directives are inert
// and resolve here without restoring the retired Microsoft.Data.Sqlite package. WI-0149 removes
// the old provider-prefixed identifiers as the fixture names are normalized.
namespace Microsoft.Data.Sqlite;

internal static class RetiredProviderNamespaceMarker
{
}
