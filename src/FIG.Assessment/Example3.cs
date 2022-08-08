using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FIG.Assessment;

/// <summary>
/// In this example, we are writing a service that will run (potentially as a windows service or elsewhere) and once a day will run a report on all new
/// users who were created in our system within the last 24 hours, as well as all users who deactivated their account in the last 24 hours. We will then
/// email this report to the executives so they can monitor how our user base is growing.
/// </summary>

/*1)Reporting service is registered as singleton class which means it can only be instantiated once. So the dbcontext will be
the same after 24 hrs for each iteration in the loop. New users will be cached but a user who is deactivated won’t show up
as being deactivated since EF will throw away the updated data. 
http://www.referencebits.com/2009/03/entity-framework-patterns-identity-map.html

2)Lines 43 -46 we’re 2 different queries in parallel with one another but will that make it faster? Is faster than
just running a single for query both deactivated and new users? Can we test it to see if it is? This is an extremely 
simple query. For a monster query with perhaps 6 other subqueries, I suspect a heavy degree of parallelism is already built into the sql server…
There is documentation I’ve found which says you can specify degrees of parallelism in each query:
https://www.sqlshack.com/use-parallel-insert-sql-server-2016-improve-query-performance/

3)Why do we need to store the results in 2 separate variables? It looks like there maybe more overhead involved. 
Could we return a single datatable with a list of both deactivated and new users, then convert it to an Excel spreadsheet and then e-mail it. Seems like less work. 

4)Why do we need to use a window service, .net, parallel programming, entity framework? Why not just  use the schedule jobs feature on the  sql server?
https://docs.microsoft.com/en-us/sql/ssms/agent/schedule-a-job?view=sql-server-ver16
and use a sp to send the e-mail out the execs? Are we not adding extra layers of complexity just by using 
5 different technologies, when we only need one?
*/
public class Example3
{
    public static void Main(string[] args)
    {
        Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddDbContext<MyContext>(options =>
                {
                    options.UseSqlServer("dummy-connection-string");
                });
                services.AddSingleton<ReportEngine>();
                services.AddHostedService<DailyReportService>();
            })
            .Build()
            .Run();
    }
}

public class DailyReportService : BackgroundService
{
    private readonly ReportEngine _reportEngine;

    public DailyReportService(ReportEngine reportEngine) => _reportEngine = reportEngine;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // when the service starts up, start by looking back at the last 24 hours
        var startingFrom = DateTime.Now.AddDays(-1);

        while (!stoppingToken.IsCancellationRequested)
        {
            var newUsersTask = this._reportEngine.GetNewUsersAsync(startingFrom);
            var deactivatedUsersTask = this._reportEngine.GetDeactivatedUsersAsync(startingFrom);
            await Task.WhenAll(newUsersTask, deactivatedUsersTask); // run both queries in parallel to save time

            // send report to execs
            await this.SendUserReportAsync(newUsersTask.Result, deactivatedUsersTask.Result);

            // save the current time, wait 24hr, and run the report again - using the new cutoff date
            startingFrom = DateTime.Now;
            await Task.Delay(TimeSpan.FromHours(24));
        }
    }

    private Task SendUserReportAsync(IEnumerable<User> newUsers, IEnumerable<User> deactivatedUsers)
    {
        // not part of this example
        return Task.CompletedTask;
    }
}

/// <summary>
/// A dummy report engine that runs queries and returns results.
/// The queries here a simple but imagine they might be complex SQL queries that could take a long time to complete.
/// </summary>
public class ReportEngine
{
    private readonly MyContext _db;

    public ReportEngine(MyContext db) => _db = db;

    public async Task<IEnumerable<User>> GetNewUsersAsync(DateTime startingFrom)
    {
        var newUsers = (await this._db.Users.ToListAsync())
            .Where(u => u.CreatedAt > startingFrom);
        return newUsers;
    }

    public async Task<IEnumerable<User>> GetDeactivatedUsersAsync(DateTime startingFrom)
    {
        var deactivatedUsers = (await this._db.Users.ToListAsync())
            .Where(u => u.DeactivatedAt > startingFrom);
        return deactivatedUsers;
    }
}

#region Database Entities
// a dummy EFCore dbcontext - not concerned with actually setting up connection strings or configuring the context in this example
public class MyContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class User
{
    public int UserId { get; set; }

    public string UserName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DeactivatedAt { get; set; }
}
#endregion
