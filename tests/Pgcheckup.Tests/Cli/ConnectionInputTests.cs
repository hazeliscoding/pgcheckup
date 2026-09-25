using Npgsql;
using Pgcheckup.Cli;

namespace Pgcheckup.Tests.Cli;

public class ConnectionInputTests
{
    private static readonly Dictionary<string, string?> NoEnvironment = [];

    private static NpgsqlConnectionStringBuilder Parse(string? input, Dictionary<string, string?>? environment = null) =>
        ConnectionInput.Parse(input, environment ?? NoEnvironment);

    [Fact]
    public void Reads_a_postgres_url()
    {
        var settings = Parse("postgres://checkup:s%40cret@db.example.com:5433/app?sslmode=verify-full");

        Assert.Equal("db.example.com", settings.Host);
        Assert.Equal(5433, settings.Port);
        Assert.Equal("app", settings.Database);
        Assert.Equal("checkup", settings.Username);
        Assert.Equal("s@cret", settings.Password);
        Assert.Equal(SslMode.VerifyFull, settings.SslMode);
    }

    [Fact]
    public void Reads_a_postgresql_url_without_user_or_port()
    {
        var settings = Parse("postgresql://db.example.com/app");

        Assert.Equal("db.example.com", settings.Host);
        Assert.Equal(5432, settings.Port);
        Assert.Equal("app", settings.Database);
        Assert.Null(settings.Username);
    }

    [Fact]
    public void Reads_ipv6_and_several_hosts()
    {
        Assert.Equal("::1", Parse("postgres://[::1]:5432/app").Host);
        Assert.Equal("h1.example.com:5432,h2.example.com:5433", Parse("postgres://h1.example.com:5432,h2.example.com:5433/app").Host);
    }

    [Fact]
    public void Reads_a_socket_directory_from_the_host_parameter()
    {
        var settings = Parse("postgres:///app?host=%2Fvar%2Frun%2Fpostgresql");

        Assert.Equal("/var/run/postgresql", settings.Host);
        Assert.Equal("app", settings.Database);
    }

    [Fact]
    public void Maps_libpq_parameters_to_npgsql()
    {
        var settings = Parse("postgres://db.example.com/app?connect_timeout=10&target_session_attrs=read-write&sslrootcert=ca.pem&sslcert=client.pem&sslkey=client.key&passfile=pass.conf");

        Assert.Equal(10, settings.Timeout);
        Assert.Equal("read-write", settings.TargetSessionAttributes);
        Assert.Equal("ca.pem", settings.RootCertificate);
        Assert.Equal("client.pem", settings.SslCertificate);
        Assert.Equal("client.key", settings.SslKey);
        Assert.Equal("pass.conf", settings.Passfile);
    }

    [Fact]
    public void Ignores_the_application_name_because_pgcheckup_sets_its_own()
    {
        Assert.Null(Parse("postgres://db.example.com/app?application_name=myapp").ApplicationName);
    }

    [Fact]
    public void Reads_a_libpq_key_value_string()
    {
        var settings = Parse("host=db.example.com port=5433 dbname=app user=checkup sslmode=require password='a b\\'c'");

        Assert.Equal("db.example.com", settings.Host);
        Assert.Equal(5433, settings.Port);
        Assert.Equal("app", settings.Database);
        Assert.Equal("checkup", settings.Username);
        Assert.Equal(SslMode.Require, settings.SslMode);
        Assert.Equal("a b'c", settings.Password);
    }

    [Fact]
    public void Fills_what_the_input_leaves_out_from_pg_environment_variables()
    {
        var environment = new Dictionary<string, string?>
        {
            ["PGHOST"] = "db.example.com",
            ["PGPORT"] = "6543",
            ["PGDATABASE"] = "app",
            ["PGSSLMODE"] = "require",
        };

        var fromEnvironment = Parse(null, environment);
        Assert.Equal("db.example.com", fromEnvironment.Host);
        Assert.Equal(6543, fromEnvironment.Port);
        Assert.Equal("app", fromEnvironment.Database);
        Assert.Equal(SslMode.Require, fromEnvironment.SslMode);

        var mixed = Parse("postgres://other.example.com/app", environment);
        Assert.Equal("other.example.com", mixed.Host);
        Assert.Equal(6543, mixed.Port);
    }

    [Fact]
    public void Connects_to_localhost_when_nothing_names_a_host()
    {
        Assert.Equal("localhost", Parse(null).Host);
        Assert.Equal("localhost", Parse("dbname=app").Host);
    }

    [Fact]
    public void Reads_a_password_with_a_question_mark_as_psql_does()
    {
        // libpq takes everything up to the first @ that comes before any / as credentials.
        Assert.Equal("hun?ter2", Parse("postgres://checkup:hun?ter2@db.example.com/app").Password);
    }

    [Theory]
    [InlineData("postgres://db.example.com/app?colour=blue", "parameter pgcheckup doesn't read")]
    [InlineData("host=db.example.com colour=blue", "parameter pgcheckup doesn't read")]
    [InlineData("postgres://db.example.com/app?sslmode=sometimes", "verify-full")]
    [InlineData("postgres://db.example.com:abc/app", "port")]
    [InlineData("mysql://db.example.com/app", "postgres://")]
    [InlineData("host=db.example.com password='unterminated", "quote")]
    [InlineData("host=db.example.com dbname", "key=value")]
    public void Explains_what_is_wrong_with_the_input(string input, string expected)
    {
        var error = Assert.Throws<ConnectionInputException>(() => Parse(input));

        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("postgres://checkup:hunter2@db.example.com:abc/app", "hunter2", "abc")]
    [InlineData("postgres://checkup:hun/ter2@db.example.com/app", "hun", "ter2")]
    [InlineData("host=db.example.com password=hunter2 colour=blue", "hunter2", "colour")]
    [InlineData("host=db.example.com password=hun ter2", "hun", "ter2")]
    [InlineData("host=db.example.com port=hunter2", "hunter2", "hunter2")]
    [InlineData("host=db.example.com sslmode=hunter2", "hunter2", "hunter2")]
    public void Never_repeats_any_of_the_input_in_an_error(string input, string first, string second)
    {
        var error = Assert.Throws<ConnectionInputException>(() => Parse(input));

        Assert.DoesNotContain(first, error.Message);
        Assert.DoesNotContain(second, error.Message);
    }
}
