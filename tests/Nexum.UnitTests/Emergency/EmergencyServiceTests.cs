using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NetTopologySuite.Geometries;
using Nexum.Modules.Emergency.Application;
using Nexum.Modules.Emergency.Domain.Entities;
using Nexum.SharedKernel.Interfaces;
using Xunit;

namespace Nexum.UnitTests.Emergency;

public sealed class EmergencyServiceTests : IDisposable
{
    private readonly Nexum.Modules.Auth.Infrastructure.Persistence.NexumDbContext _db;
    private readonly Mock<INotificationService> _notificationsMock;
    private readonly IEmergencyService _sut;

    public EmergencyServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _notificationsMock = new Mock<INotificationService>();
        _sut = new EmergencyService(_db, _notificationsMock.Object,
            new Mock<ILogger<EmergencyService>>().Object);
    }

    [Fact]
    public async Task CreateReport_WithValidMedicalRequest_ShouldCreateIncident()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var request = new ReportRequest("medical", 6.8403, 3.3864, "Test emergency");

        // Act
        var result = await _sut.CreateReportAsync(userId, request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.ReportType.Should().Be("Medical");
        result.Data.PatientId.Should().Be(userId);
        result.Data.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task CreateReport_WithInvalidReportType_ShouldReturnError()
    {
        // Arrange
        var request = new ReportRequest("invalid_type", 6.84, 3.38, null);

        // Act
        var result = await _sut.CreateReportAsync("user1", request);

        // Assert
        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("INVALID_REPORT_TYPE");
    }

    [Fact]
    public async Task CreateReport_WithNearbyOfficer_ShouldAutoDispatch()
    {
        // Arrange
        var officerId = Guid.NewGuid().ToString();
        var officerUser = new Nexum.Modules.Auth.Domain.Entities.ApplicationUser
        {
            Id = officerId, Email = "officer@test.com",
            FullName = "Dr. Test", FcmToken = "test-fcm-token"
        };
        _db.Users.Add(officerUser);

        // Add officer location very close to incident
        _db.OfficerLocations.Add(new Nexum.Modules.Auth.Infrastructure.Persistence.OfficerLocation
        {
            UserId = officerId,
            Location = new Point(3.3865, 6.8403) { SRID = 4326 }, // ~10m away
            IsAvailable = true,
            LastSeenAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _notificationsMock.Setup(n => n.SendPushAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new ReportRequest("medical", 6.8403, 3.3864, "Emergency near officer");

        // Act
        var result = await _sut.CreateReportAsync("patient1", request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be("Dispatched");
        result.Data.AssignedOfficerId.Should().Be(officerId);

        _notificationsMock.Verify(n => n.SendPushAsync(
            "test-fcm-token",
            It.Is<string>(s => s.Contains("Dispatch")),
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_OnResolvedIncident_ShouldReturnError()
    {
        // Arrange
        var incident = new SosIncident
        {
            PatientId = "user1",
            ReportType = ReportType.Medical,
            PatientLocation = new Point(3.38, 6.84) { SRID = 4326 },
            Status = IncidentStatus.Resolved
        };
        _db.SosIncidents.Add(incident);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.UpdateStatusAsync(incident.Id, "officer1",
            new UpdateIncidentStatusRequest("EnRoute", null));

        // Assert
        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("INCIDENT_ALREADY_RESOLVED");
    }

    [Fact]
    public async Task GetIncidents_MedicalOfficer_ShouldOnlySeemedicalIncidents()
    {
        // Arrange
        _db.SosIncidents.AddRange(
            new SosIncident { PatientId = "p1", ReportType = ReportType.Medical,
                PatientLocation = new Point(3.38, 6.84) { SRID = 4326 } },
            new SosIncident { PatientId = "p2", ReportType = ReportType.Security,
                PatientLocation = new Point(3.38, 6.84) { SRID = 4326 } }
        );
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetIncidentsAsync("officer1", "medical_officer", 1, 20);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().AllSatisfy(i => i.ReportType.Should().Be("Medical"));
    }

    public void Dispose() => _db.Dispose();
}
