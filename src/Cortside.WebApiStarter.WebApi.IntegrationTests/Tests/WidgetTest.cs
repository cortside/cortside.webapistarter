using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Cortside.WebApiStarter.WebApi.IntegrationTests.Tests {
    public class WidgetTest : IClassFixture<TestWebApplicationFactory<Startup>> {
        private readonly TestWebApplicationFactory<Startup> fixture;
        private readonly ITestOutputHelper testOutputHelper;
        private readonly HttpClient testServerClient;

        public WidgetTest(TestWebApplicationFactory<Startup> fixture, ITestOutputHelper testOutputHelper) {
            this.fixture = fixture;
            this.testOutputHelper = testOutputHelper;
            testServerClient = fixture.CreateClient(new WebApplicationFactoryClientOptions {
                AllowAutoRedirect = false
            });
        }

        [Fact]
        public async Task Test() {
            //arrange
            var id = fixture.Db.Widgets.First().WidgetId;

            //act
            var response = await testServerClient.GetAsync($"api/v1/widgets/{id}").ConfigureAwait(false);

            //assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
