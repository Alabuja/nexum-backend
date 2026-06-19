using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NetTopologySuite.Geometries;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.MissingPersons.Application;
using Nexum.Modules.MissingPersons.Domain.Entities;
using Nexum.Modules.Parking.Application;
using Nexum.Modules.Parking.Domain.Entities;
using Nexum.SharedKernel.Interfaces;
using Xunit;

namespace Nexum.UnitTests.Parking
{

    public sealed class ParkingServiceTests : IDisposable
    {
        private readonly NexumDbContext _db;
        private readonly Mock<INotificationService> _notificationsMock;
        private readonly IParkingService _sut;

        public ParkingServiceTests()
        {
            _db = TestDbContextFactory.Create();
            _notificationsMock = new Mock<INotificationService>();
            _sut = new ParkingService(_db, _notificationsMock.Object,
                new Mock<ILogger<ParkingService>>().Object);
        }

        [Fact]
        public async Task CreatePin_FirstPin_ShouldSucceed()
        {
            // Arrange
            var request = new CreatePinRequest(6.8389, 3.3901, "Lot G", "Silver Camry", "KJA-234", null);

            // Act
            var result = await _sut.CreatePinAsync("user1", request);

            // Assert
            result.Success.Should().BeTrue();
            result.Data!.UserId.Should().Be("user1");
            result.Data.LicencePlate.Should().Be("KJA-234");
            result.Data.IsActive.Should().BeTrue();
        }

        [Fact]
        public async Task CreatePin_SecondActivePin_ShouldReturnError()
        {
            // Arrange
            _db.ParkingPins.Add(new ParkingPin
            {
                UserId = "user1",
                PinLocation = new Point(3.39, 6.84) { SRID = 4326 },
                IsActive = true
            });
            await _db.SaveChangesAsync();

            // Act
            var result = await _sut.CreatePinAsync("user1",
                new CreatePinRequest(6.84, 3.39, null, null, null, null));

            // Assert
            result.Success.Should().BeFalse();
            result.Error!.Code.Should().Be("PIN_ALREADY_ACTIVE");
        }

        [Fact]
        public async Task DeactivatePin_OwnPin_ShouldSucceed()
        {
            // Arrange
            var pin = new ParkingPin
            {
                UserId = "user1",
                PinLocation = new Point(3.39, 6.84) { SRID = 4326 },
                IsActive = true
            };
            _db.ParkingPins.Add(pin);
            await _db.SaveChangesAsync();

            // Act
            var result = await _sut.DeactivatePinAsync("user1", pin.Id);

            // Assert
            result.Success.Should().BeTrue();
            pin.IsActive.Should().BeFalse();
        }

        [Fact]
        public async Task DeactivatePin_OtherUserPin_ShouldReturnNotFound()
        {
            // Arrange
            var pin = new ParkingPin
            {
                UserId = "user2",
                PinLocation = new Point(3.39, 6.84) { SRID = 4326 },
                IsActive = true
            };
            _db.ParkingPins.Add(pin);
            await _db.SaveChangesAsync();

            // Act
            var result = await _sut.DeactivatePinAsync("user1", pin.Id); // different user

            // Assert
            result.Success.Should().BeFalse();
            result.Error!.Code.Should().Be("PIN_NOT_FOUND");
        }

        [Fact]
        public async Task CreateBlockingAlert_WithNearbyPin_ShouldNotifyOwner()
        {
            // Arrange
            var blockingOwner = new Nexum.Modules.Auth.Domain.Entities.ApplicationUser
            {
                Id = "owner1",
                Email = "owner@test.com",
                FullName = "Owner",
                FcmToken = "owner-fcm-token"
            };
            _db.Users.Add(blockingOwner);

            _db.ParkingPins.Add(new ParkingPin
            {
                UserId = "owner1",
                PinLocation = new Point(3.3901, 6.8389) { SRID = 4326 }, // same spot
                IsActive = true,
                LicencePlate = "BLOCKER-01"
            });
            await _db.SaveChangesAsync();

            _notificationsMock.Setup(n => n.SendPushAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var request = new CreateBlockingAlertRequest(6.8389, 3.3901, "Blocking my exit");

            // Act
            var result = await _sut.CreateBlockingAlertAsync("reporter1", request);

            // Assert
            result.Success.Should().BeTrue();
            _notificationsMock.Verify(n => n.SendPushAsync(
                "owner-fcm-token",
                It.Is<string>(s => s.Contains("blocking")),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateBlockingAlert_WithLicencePlate_ShouldMatchPinnedVehicle()
        {
            // Arrange
            var blockingOwner = new Nexum.Modules.Auth.Domain.Entities.ApplicationUser
            {
                Id = "owner2",
                Email = "owner2@test.com",
                FullName = "Owner Two",
                FcmToken = "owner2-fcm-token"
            };
            _db.Users.Add(blockingOwner);

            _db.ParkingPins.Add(new ParkingPin
            {
                UserId = "owner2",
                PinLocation = new Point(3.5000, 6.9000) { SRID = 4326 },
                IsActive = true,
                LicencePlate = "JHS-234-BG"
            });
            await _db.SaveChangesAsync();

            _notificationsMock.Setup(n => n.SendPushAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var request = new CreateBlockingAlertRequest(
                6.8389, 3.3901, "Blocking my exit", "jhs 234 bg", "Black Tesla");

            // Act
            var result = await _sut.CreateBlockingAlertAsync("reporter1", request);

            // Assert
            result.Success.Should().BeTrue();
            result.Data!.BlockerPinId.Should().NotBeNull();
            _notificationsMock.Verify(n => n.SendPushAsync(
                "owner2-fcm-token",
                It.Is<string>(s => s.Contains("blocking")),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        public void Dispose() => _db.Dispose();
    }
}

namespace Nexum.UnitTests.MissingPersons
{

    public sealed class MissingPersonServiceTests : IDisposable
    {
        private readonly Nexum.Modules.Auth.Infrastructure.Persistence.NexumDbContext _db;
        private readonly Mock<INotificationService> _notificationsMock;
        private readonly IMissingPersonService _sut;

        public MissingPersonServiceTests()
        {
            _db = TestDbContextFactory.Create();
            _notificationsMock = new Mock<INotificationService>();
            _sut = new MissingPersonService(_db, _notificationsMock.Object,
                new Mock<ILogger<MissingPersonService>>().Object);
        }

        [Fact]
        public async Task CreateAlert_ShouldBroadcastToAllDevices()
        {
            // Arrange
            _db.Users.AddRange(
                new Nexum.Modules.Auth.Domain.Entities.ApplicationUser
                { Id = "u1", Email = "u1@t.com", FcmToken = "token1" },
                new Nexum.Modules.Auth.Domain.Entities.ApplicationUser
                { Id = "u2", Email = "u2@t.com", FcmToken = "token2" }
            );
            await _db.SaveChangesAsync();

            _notificationsMock.Setup(n => n.SendPushToManyAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var request = new CreateAlertRequest("Chisom Eze", 7, "Blue dress", null, 6.84, 3.38, "Near Gate B");

            // Act
            var result = await _sut.CreateAlertAsync("reporter1", request);

            // Assert
            result.Success.Should().BeTrue();
            result.Data!.FullName.Should().Be("Chisom Eze");

            _notificationsMock.Verify(n => n.SendPushToManyAsync(
                It.Is<IEnumerable<string>>(tokens => tokens.Count() == 2),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateSighting_OnClosedAlert_ShouldReturnError()
        {
            // Arrange
            var alert = new MissingPersonAlert
            {
                ReportedBy = "user1",
                FullName = "Test",
                Description = "Test",
                Status = AlertStatus.Closed
            };
            _db.MissingPersonAlerts.Add(alert);
            await _db.SaveChangesAsync();

            // Act
            var result = await _sut.CreateSightingAsync("user2", alert.Id,
                new CreateSightingRequest(6.84, 3.38, "Near gate", null));

            // Assert
            result.Success.Should().BeFalse();
            result.Error!.Code.Should().Be("ALERT_ALREADY_CLOSED");
        }

        public void Dispose() => _db.Dispose();
    }
}
