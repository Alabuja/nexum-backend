using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexum.IntegrationTests.Common;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexum.IntegrationTests.Common
{

    public sealed class NexumWebFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove real DB, use InMemory
                var descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<NexumDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<NexumDbContext>(opts =>
                    opts.UseInMemoryDatabase("nexum_integration_tests"));

                // Override Firebase (no real credentials in tests)
                // Notifications are mocked via the DI replacement below
            });

            builder.UseEnvironment("Testing");
        }
    }
}

namespace Nexum.IntegrationTests.Auth
{

    public sealed class AuthApiTests : IClassFixture<NexumWebFactory>
    {
        private readonly HttpClient _client;

        public AuthApiTests(NexumWebFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Register_ValidRequest_Returns200WithTokens()
        {
            // Arrange
            var request = new
            {
                fullName = "Integration Tester",
                email = $"test_{Guid.NewGuid():N}@test.com",
                password = "Password123!",
                phoneNumber = "+2348012345678",
                emergencyContactName = "Test Contact",
                emergencyContactPhone = "+2348098765432",
                estateOrZone = "Test Zone"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/v1/auth/register", request);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.GetProperty("success").GetBoolean().Should().BeTrue();
            body.GetProperty("data").GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
            body.GetProperty("data").GetProperty("user").GetProperty("role").GetString()
                .Should().Be("worshipper");
        }

        [Fact]
        public async Task Register_DuplicateEmail_Returns400()
        {
            // Arrange
            var email = $"dup_{Guid.NewGuid():N}@test.com";
            var request = new { fullName = "Test", email, password = "Password123!" };

            // Act
            await _client.PostAsJsonAsync("/v1/auth/register", request); // first
            var response = await _client.PostAsJsonAsync("/v1/auth/register", request); // duplicate
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            body.GetProperty("error").GetProperty("code").GetString()
                .Should().Be("EMAIL_ALREADY_REGISTERED");
        }

        [Fact]
        public async Task Login_ValidCredentials_Returns200WithTokens()
        {
            // Arrange — register first
            var email = $"login_{Guid.NewGuid():N}@test.com";
            var password = "Password123!";
            await _client.PostAsJsonAsync("/v1/auth/register",
                new { fullName = "Login Test", email, password });

            // Act
            var response = await _client.PostAsJsonAsync("/v1/auth/login",
                new { email, password });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.GetProperty("data").GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
            body.GetProperty("data").GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Login_WrongPassword_Returns401()
        {
            // Arrange
            var email = $"wrongpw_{Guid.NewGuid():N}@test.com";
            await _client.PostAsJsonAsync("/v1/auth/register",
                new { fullName = "Test", email, password = "Password123!" });

            // Act
            var response = await _client.PostAsJsonAsync("/v1/auth/login",
                new { email, password = "WrongPassword!" });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            body.GetProperty("error").GetProperty("code").GetString()
                .Should().Be("INVALID_CREDENTIALS");
        }

        [Fact]
        public async Task GetMe_WithoutToken_Returns401()
        {
            var response = await _client.GetAsync("/v1/auth/me");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task GetMe_WithValidToken_Returns200WithProfile()
        {
            // Arrange — register and get token
            var email = $"me_{Guid.NewGuid():N}@test.com";
            var registerResponse = await _client.PostAsJsonAsync("/v1/auth/register",
                new { fullName = "Me Test", email, password = "Password123!" });
            var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
            var token = registerBody.GetProperty("data").GetProperty("accessToken").GetString()!;

            // Act
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await _client.GetAsync("/v1/auth/me");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.GetProperty("data").GetProperty("email").GetString().Should().Be(email);

            _client.DefaultRequestHeaders.Authorization = null;
        }

        [Fact]
        public async Task ForgotPassword_AnyEmail_AlwaysReturns200()
        {
            // Should always 200 to prevent enumeration
            var response = await _client.PostAsJsonAsync("/v1/auth/forgot-password",
                new { email = "definitely_not_registered@test.com" });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

namespace Nexum.IntegrationTests.Emergency
{

    public sealed class EmergencyApiTests : IClassFixture<NexumWebFactory>
    {
        private readonly HttpClient _client;
        private readonly NexumWebFactory _factory;

        public EmergencyApiTests(NexumWebFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private async Task<string> GetTokenAsync(string role = "worshipper")
        {
            var email = $"test_{Guid.NewGuid():N}@test.com";
            var response = await _client.PostAsJsonAsync("/v1/auth/register",
                new { fullName = "Test User", email, password = "Password123!" });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("data").GetProperty("accessToken").GetString()!;
        }

        [Fact]
        public async Task Report_WithoutGeofenceHeaders_Returns400()
        {
            var token = await GetTokenAsync();
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // No X-Client-Lat/Lng headers
            var response = await _client.PostAsJsonAsync("/v1/emergency/report",
                new { reportType = "medical", latitude = 6.84, longitude = 3.38, description = "Test" });

            // Without geofence headers the middleware returns 400
            response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden);
            _client.DefaultRequestHeaders.Authorization = null;
        }

        [Fact]
        public async Task Report_ValidMedicalRequest_Returns200()
        {
            var token = await GetTokenAsync();
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            _client.DefaultRequestHeaders.Add("X-Client-Lat", "6.8403");
            _client.DefaultRequestHeaders.Add("X-Client-Lng", "3.3864");

            var response = await _client.PostAsJsonAsync("/v1/emergency/report",
                new { reportType = "medical", latitude = 6.8403, longitude = 3.3864 });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.GetProperty("success").GetBoolean().Should().BeTrue();

            _client.DefaultRequestHeaders.Authorization = null;
            _client.DefaultRequestHeaders.Remove("X-Client-Lat");
            _client.DefaultRequestHeaders.Remove("X-Client-Lng");
        }
    }
}