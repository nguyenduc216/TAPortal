using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TAPortal.DbMcp.Options;

namespace TAPortal.DbMcp.Data;

public sealed class SqlConnectionFactory
{
    private readonly DatabaseOptions _options;

    public SqlConnectionFactory(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;
    }

    public SqlConnection Create()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Database connection string is not configured. Set Database__ConnectionString as an environment variable.");
        }

        return new SqlConnection(_options.ConnectionString);
    }

    public int CommandTimeoutSeconds => Math.Clamp(_options.CommandTimeoutSeconds, 5, 120);
    public int MaxRows => Math.Clamp(_options.MaxRows, 1, 1000);
}
