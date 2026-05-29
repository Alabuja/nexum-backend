using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Nexum.Modules.Auth.Application.DTOs;
using Nexum.Modules.Auth.Application.Services;
using Nexum.Modules.Auth.Domain.Entities;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.SharedKernel.Interfaces;
using Xunit;

namespace Nexum.UnitTests.Auth;

public sealed class AuthServiceTests : IDisposable
{
    private readonly NexumDbContext _db;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<RoleManager<IdentityRole>> _roleManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<IEmailService> _emailMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly IAuthService _sut;

    public AuthServiceTests()
    {
        _db = TestDbContextFactory.Create();

        var userStoreMock = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var roleStoreMock = new Mock<IRoleStore<IdentityRole>>();
        _roleManagerMock = new Mock<RoleManager<IdentityRole>>(
            roleStoreMock.Object, null!, null!, null!, null!);

        var contextAccessorMock = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var userPrincipalFactoryMock = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            _userManagerMock.Object, contextAccessorMock.Object,
            userPrincipalFactoryMock.Object, null!, null!, null!, null!);

        _emailMock = new Mock<IEmailService>();
        _configMock = new Mock<IConfiguration>();

        // Setup JWT config
        var jwtSection = new Mock<IConfigurationSection>();
        jwtSection.Setup(s => s["Secret"]).Returns("test-secret-key-that-is-long-enough-32chars");
        jwtSection.Setup(s => s["Issuer"]).Returns("test-issuer");
        jwtSection.Setup(s => s["Audience"]).Returns("test-audience");
        _configMock.Setup(c => c["Jwt:Secret"]).Returns("test-secret-key-that-is-long-enough-32chars");
        _configMock.Setup(c => c["Jwt:Issuer"]).Returns("test-issuer");
        _configMock.Setup(c => c["Jwt:Audience"]).Returns("test-audience");

        _sut = new AuthService(
            _userManagerMock.Object,
            _roleManagerMock.Object,
            _signInManagerMock.Object,
            _db,
            _configMock.Object,
            _emailMock.Object,
            new Mock<ILogger<AuthService>>().Object);
    }

    [Fact]
    public async Task Register_WithNewEmail_ShouldReturnSuccess()
    {
        // Arrange
        var request = new RegisterRequest(
            "Adebayo Okafor", "adebayo@test.com", "Password123!",
            "+2348012345678", "Funke Okafor", "+2348098765432", "Redemption Estate");

        _userManagerMock.Setup(m => m.FindByEmailAsync(request.Email))
            .ReturnsAsync((ApplicationUser?)null);

        var newUser = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = request.Email, FullName = request.FullName };
        _userManagerMock.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<ApplicationUser, string>((u, _) =>
            {
                u.Id = newUser.Id;
                u.Email = newUser.Email;
            });

        _userManagerMock.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), Roles.Worshipper))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync([Roles.Worshipper]);

        // Act
        var result = await _sut.RegisterAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.User.Role.Should().Be(Roles.Worshipper);
        result.Data.User.Email.Should().Be(request.Email);
    }

    [Fact]
    public async Task Register_WithExistingEmail_ShouldReturnError()
    {
        // Arrange
        var request = new RegisterRequest(
            "Test", "existing@test.com", "Password123!", null, null, null, null);

        _userManagerMock.Setup(m => m.FindByEmailAsync(request.Email))
            .ReturnsAsync(new ApplicationUser { Email = request.Email });

        // Act
        var result = await _sut.RegisterAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("EMAIL_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnTokens()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "user@test.com",
            FullName = "Test User",
            AccountStatus = AccountStatus.Active
        };

        _userManagerMock.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _userManagerMock.Setup(m => m.GetRolesAsync(user)).ReturnsAsync([Roles.Worshipper]);
        _signInManagerMock.Setup(m => m.CheckPasswordSignInAsync(user, "Password123!", true))
            .ReturnsAsync(SignInResult.Success);

        // Act
        var result = await _sut.LoginAsync(new LoginRequest(user.Email, "Password123!"));

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.AccessToken.Should().NotBeNullOrEmpty();
        result.Data.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShouldReturnError()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "user@test.com",
            AccountStatus = AccountStatus.Active
        };

        _userManagerMock.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _signInManagerMock.Setup(m => m.CheckPasswordSignInAsync(user, "wrong", true))
            .ReturnsAsync(SignInResult.Failed);

        // Act
        var result = await _sut.LoginAsync(new LoginRequest(user.Email, "wrong"));

        // Assert
        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_WithSuspendedAccount_ShouldReturnSuspendedError()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "suspended@test.com",
            AccountStatus = AccountStatus.Suspended
        };
        _userManagerMock.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);

        // Act
        var result = await _sut.LoginAsync(new LoginRequest(user.Email, "Password123!"));

        // Assert
        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task ForgotPassword_WithUnknownEmail_ShouldReturn200ToPreventEnumeration()
    {
        // Arrange
        _userManagerMock.Setup(m => m.FindByEmailAsync("unknown@test.com"))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("unknown@test.com"));

        // Assert — always returns success (enumeration prevention)
        result.Success.Should().BeTrue();
        _emailMock.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_WithKnownEmail_ShouldSendOtpEmail()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "known@test.com",
            FullName = "Known User"
        };
        _userManagerMock.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _emailMock.Setup(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest(user.Email));

        // Assert
        result.Success.Should().BeTrue();
        _emailMock.Verify(e => e.SendAsync(
            user.Email, user.FullName,
            It.Is<string>(s => s.Contains("Reset")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose() => _db.Dispose();
}
