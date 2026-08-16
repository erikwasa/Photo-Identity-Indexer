using Xunit;

// The integration suite creates many independent WebApplicationFactory/TestServer hosts.
// On Windows, parallel host lifetimes have repeatedly produced cross-test 500 responses
// when one host is torn down while another is still servicing a request. Keep unit-test
// assemblies parallel, but run this host-heavy integration assembly sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
