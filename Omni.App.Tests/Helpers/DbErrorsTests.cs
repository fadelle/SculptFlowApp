using Microsoft.EntityFrameworkCore;
using Npgsql;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Tests.Helpers;

/// <summary>DbErrors: which database failures are the data's fault (a 400 through InvalidRecordException) rather than a 500.</summary>
public class DbErrorsTests
{
    private static DbUpdateException Failed(string sqlState) =>
        new("save failed", new PostgresException("failed", "ERROR", "ERROR", sqlState));

    [Theory]
    [InlineData(PostgresErrorCodes.StringDataRightTruncation, InvalidRecordException.InvalidValueKey)]
    [InlineData(PostgresErrorCodes.InvalidTextRepresentation, InvalidRecordException.InvalidValueKey)] // QA t8-06: bad JSON in a jsonb column
    [InlineData(PostgresErrorCodes.CheckViolation, InvalidRecordException.InvalidValueKey)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, InvalidRecordException.MissingReferenceKey)]
    public void Bad_data_is_an_invalid_record(string sqlState, string key) =>
        Assert.Equal(key, Assert.IsType<InvalidRecordException>(DbErrors.AsInvalidRecord(Failed(sqlState))).Message);

    [Fact]
    public void A_race_or_an_outage_is_not()
    {
        Assert.Null(DbErrors.AsInvalidRecord(Failed(PostgresErrorCodes.UniqueViolation)));
        Assert.Null(DbErrors.AsInvalidRecord(Failed(PostgresErrorCodes.ConnectionFailure)));
    }
}
