using System;
using System.Linq;
using Cortside.WebApiStarter.Data;
using Cortside.WebApiStarter.WebApi.IntegrationTests.Helpers;
using Cortside.WebApiStarter.WebApi.IntegrationTests.Helpers.HotDocsMock;
using EFCore.Seeder.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Cortside.WebApiStarter.WebApi.IntegrationTests {
    public class TestWebApplicationFactory<Startup> : WebApplicationFactory<Startup> where Startup : class {
        public DatabaseContext Db { get; private set; }

        protected override IHostBuilder CreateHostBuilder() {
            var configuration = new ConfigurationBuilder()
                 .AddJsonFile("appsettings.integration.json", optional: false, reloadOnChange: true)
                 .Build();

            var server = new SampleWireMock()
                .ConfigureBuilder();

            var section = configuration.GetSection("HealthCheckHostedService");
            section["Checks:1:Value"] = server.mockServer.Urls.First();
            section["Checks:2:Value"] = server.mockServer.Urls.First();

            return Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(builder => {
                    builder.AddConfiguration(configuration);
                })
                .ConfigureWebHostDefaults(webbuilder => {
                    webbuilder
                    .UseStartup<Startup>()
                    .UseSerilog();
                });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.ConfigureServices(services => {
                // Remove the app's DbContext registration.
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType ==
                        typeof(DbContextOptions<DatabaseContext>));

                if (descriptor != null) {
                    services.Remove(descriptor);
                }

                var dbName = $"DBNAME_{Guid.NewGuid()}";
                var dbOptions = new DbContextOptionsBuilder<DatabaseContext>()
                    .UseInMemoryDatabase(databaseName: dbName)
                    .Options;

                var dbContext = new DatabaseContext(dbOptions, null);
                SeedInMemoryLoanDb(dbContext);
                services.AddSingleton(dbContext);

                this.Db = dbContext;

                // Build the service provider.
                var sp = services.BuildServiceProvider();

                // Create a scope to obtain a reference to the database
                // context (DbContext).
                using (var scope = sp.CreateScope()) {
                    var scopedServices = scope.ServiceProvider;
                    var db = scopedServices.GetRequiredService<DatabaseContext>();
                    var logger = scopedServices.GetRequiredService<ILogger<TestWebApplicationFactory<Startup>>>();

                    // Ensure the database is created.
                    db.Database.EnsureCreated();
                }
            });
        }

        private void SeedInMemoryLoanDb(DatabaseContext dbContext) {
            dbContext.Subjects.SeedFromFile(".\\SeedData\\Subject.csv");
            dbContext.SaveChanges();

            dbContext.Widgets.SeedFromFile(".\\SeedData\\Widget.csv");
            dbContext.SaveChanges();
        }
    }
}
