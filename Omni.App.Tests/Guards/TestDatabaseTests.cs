using Npgsql;

namespace Omni.App.Tests.Guards;

/// <summary>QA t0-01: the database tests timed out on Windows unless GSS encryption was turned off by hand.</summary>
public class TestDatabaseTests
{
    [Theory]
    [InlineData("Host=localhost;Port=55433;Username=postgres", GssEncryptionMode.Disable)]
    [InlineData("Host=localhost;Gss Encryption Mode=Prefer", GssEncryptionMode.Prefer)]
    [InlineData("Host=localhost;GssEncryptionMode=Require", GssEncryptionMode.Require)]
    public void Gss_encryption_is_off_unless_the_connection_string_chooses(string connectionString, GssEncryptionMode expected) =>
        Assert.Equal(expected, new NpgsqlConnectionStringBuilder(TestDatabase.WithDefaults(connectionString)).GssEncryptionMode);
}
