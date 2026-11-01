using Xunit;

namespace ProtoFast.PostgresConfig.UnitTests;

public class PlanTests
{
    private static readonly DatabaseUser Keycloak = DatabaseUser.From("Host=postgres;Port=5432;Database=keycloak;Username=keycloak;Password=k-secret");
    private static readonly DatabaseUser Auth = DatabaseUser.From("Host=postgres;Port=5432;Database=auth;Username=auth;Password=a-secret");

    [Fact]
    public void Each_database_gets_its_own_user_first()
    {
        var steps = Plan.For("postgres", [Keycloak, Auth]);

        Assert.Equal(
            ["user keycloak logs in with its connection string's password", "database keycloak is owned by keycloak",
             "user auth logs in with its connection string's password", "database auth is owned by auth"],
            steps.Select(s => s.Description));
    }

    [Fact]
    public void A_database_that_logs_in_as_the_superuser_makes_no_user()
    {
        // Under `aspire run` every connection string logs in as Aspire's postgres user.
        var local = DatabaseUser.From("Host=localhost;Port=5432;Database=protofast;Username=postgres;Password=p");

        var steps = Plan.For("postgres", [local]);

        Assert.Equal(["database protofast is owned by postgres"], steps.Select(s => s.Description));
    }

    [Fact]
    public void A_user_shared_by_two_databases_is_made_once()
    {
        var other = DatabaseUser.From("Host=postgres;Database=engine;Username=auth;Password=a-secret");

        var steps = Plan.For("postgres", [Auth, other]);

        Assert.Single(steps, s => s is EnsureUser);
    }

    [Fact]
    public void No_step_prints_a_password()
    {
        foreach (var step in Plan.For("postgres", [Keycloak, Auth]))
        {
            Assert.DoesNotContain("secret", step.ToString(), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("secret", Keycloak.ToString(), StringComparison.Ordinal);
    }
}
