using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nexum.Api.Hubs;
using Nexum.Api.Infrastructure;
using Nexum.Api.Jobs;
using Nexum.Api.Middleware;
using Nexum.Modules.Auth.Application.Services;
using Nexum.Modules.Auth.Domain.Entities;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Booking.Application.Services;
using Nexum.Modules.Emergency.Application;
using Nexum.Modules.MissingPersons.Application;
using Nexum.Modules.Parking.Application;
using Nexum.Modules.Transit.Application;
using Nexum.Modules.Transit.Application.Services;
using Nexum.SharedKernel.Geofence;
using Nexum.SharedKernel.Interfaces;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/nexum-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30));

// ── Database ──────────────────────────────────────────────────
builder.Services.AddDbContext<NexumDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql =>
        {
            npgsql.UseNetTopologySuite();
            npgsql.CommandTimeout(60); // bumped from default 30s
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
            npgsql.MigrationsAssembly("Nexum.Api");
        }));

// ── Identity ──────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(opts =>
{
    opts.Password.RequireDigit = true;
    opts.Password.RequiredLength = 8;
    opts.Password.RequireUppercase = true;
    opts.Password.RequireNonAlphanumeric = true;
    opts.Lockout.MaxFailedAccessAttempts = 5;
    opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    opts.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<NexumDbContext>()
.AddDefaultTokenProviders();

// ── Authentication — JWT + Cookie ─────────────────────────────
var jwtKey = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(opts =>
{
    opts.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ClockSkew = TimeSpan.Zero
    };
    // Allow JWT from SignalR query string
    opts.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var accessToken = ctx.Request.Query["access_token"];
            var path = ctx.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                ctx.Token = accessToken;
            return Task.CompletedTask;
        }
    };
})
.AddCookie("NexumCookie", opts =>
{
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.Cookie.SameSite = SameSiteMode.Strict;
    opts.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    opts.SlidingExpiration = true;
});

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────
//builder.Services.AddCors(opts => opts.AddPolicy("NexumPolicy", policy =>
//    policy.WithOrigins(
//        builder.Configuration["Cors:WebPortalUrl"],
//        builder.Configuration["Cors:TestPortalUrl"],
//        builder.Configuration["Cors:MobileDevUrl"],
//        builder.Configuration["Cors:TestMobileDevUrl"])
//    .AllowAnyMethod()
//    .AllowAnyHeader()));

builder.Services.AddCors(opts => opts.AddPolicy("NexumPolicy", policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader()));

// ── SignalR ───────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── Hangfire ──────────────────────────────────────────────────
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        builder.Configuration.GetConnectionString("DefaultConnection")!));
builder.Services.AddHangfireServer();

// ── Health Checks ─────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgres", tags: ["ready"]);

// ── Module services ───────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmergencyService, EmergencyService>();
builder.Services.AddScoped<IMissingPersonService, MissingPersonService>();
builder.Services.AddScoped<IParkingService, ParkingService>();
builder.Services.AddScoped<ITransitService, TransitService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();

// ── Booking services ──────────────────────────────────────────
builder.Services.AddSingleton<BookingLockRegistry>(); // Singleton for SemaphoreSlim registry
builder.Services.AddScoped<IPropertyService, PropertyService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<IHostApplicationService, HostApplicationService>();
builder.Services.AddScoped<IPaystackService, PaystackService>();
builder.Services.AddHttpClient<IPaystackService, PaystackService>();
builder.Services.AddScoped<IHostBankAccountService, HostBankAccountService>();
builder.Services.AddScoped<IPayoutService, PayoutService>();
builder.Services.AddScoped<PayoutService>(); // concrete for Hangfire
builder.Services.AddHttpClient<IPaystackTransferService, PaystackTransferService>();

// ── Infrastructure services ───────────────────────────────────
builder.Services.AddSingleton<IGeofenceService, GeofenceService>();
builder.Services.AddScoped<IGeofenceRepository, GeofenceRepository>();
builder.Services.AddScoped<INotificationService, FcmNotificationService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IImageUploadService, CloudinaryImageUploadService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddHttpContextAccessor();

// Hub notification services
builder.Services.AddScoped<IEmergencyHubService, EmergencyHubService>();
builder.Services.AddScoped<IMissingPersonsHubService, MissingPersonsHubService>();
builder.Services.AddScoped<IShuttleHubService, ShuttleHubService>();

// ── Controllers ───────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(opts => {
    opts.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Nexum API",
        Version = "v1",
        Description = "",
    });
    opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });
    opts.AddSecurityRequirement(new OpenApiSecurityRequirement {{
        new OpenApiSecurityScheme {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }, Array.Empty<string>()
    }});
});

var app = builder.Build();

// ── Firebase init ─────────────────────────────────────────────
var firebaseCredPath = app.Configuration["Firebase:CredentialPath"];
if (firebaseCredPath is not null && File.Exists(firebaseCredPath))
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(firebaseCredPath)
    });
}

// ── DB migration + seed ───────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NexumDbContext>();
    await db.Database.MigrateAsync();

    // Seed roles
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    foreach (var role in Roles.All)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Seed geofence — default Redemption City boundary
    //if (!db.GeofenceZones.Any())
    //{
    //    // Approximate Redemption City boundary polygon (WGS84)
    //    var wkt = "POLYGON((3.3750 6.8300, 3.4050 6.8300, 3.4050 6.8500, 3.3750 6.8500, 3.3750 6.8300))";
    //    var reader = new NetTopologySuite.IO.WKTReader();
    //    db.GeofenceZones.Add(new()
    //    {
    //        Name = "Redemption City — Default Boundary",
    //        Description = "Default camp boundary — update via admin portal",
    //        Boundary = reader.Read(wkt),
    //        IsActive = true,
    //        ActivatedAt = DateTime.UtcNow
    //    });
    //    await db.SaveChangesAsync();
    //}
    await DatabaseSeeder.SeedAsync(db, userManager, roleManager);
    // Warm geofence cache
    var geofenceService = scope.ServiceProvider.GetRequiredService<IGeofenceService>();
    await geofenceService.RefreshAsync();
}

// ── Middleware pipeline ───────────────────────────────────────
////if (app.Environment.IsDevelopment())
////{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseCors("NexumPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<GeofenceMiddleware>();

// Health checks
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Hangfire dashboard (admin only in production)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthFilter()]
});

app.MapControllers();
app.MapHub<EmergencyHub>("/hubs/emergency");
app.MapHub<MissingPersonsHub>("/hubs/missing-persons");
app.MapHub<ShuttleHub>("/hubs/shuttle");

// Register recurring jobs
EscalationJobs.RegisterRecurringJobs();

// ── Hangfire: expired booking cleanup ─────────────────────────
RecurringJob.AddOrUpdate<BookingService>(
    "expire-pending-bookings",
    svc => svc.ExpireOldBookingsAsync(default),
    "*/5 * * * *"); // every 5 minutes

// Run daily at 06:00 UTC — process payouts for bookings 3+ days old
RecurringJob.AddOrUpdate<PayoutService>(
    "process-due-payouts",
    svc => svc.ProcessDuePayoutsAsync(default),
    "0 6 * * *"); // 06:00 UTC daily (07:00 WAT)

RecurringJob.AddOrUpdate<IGeofenceService>(
    "db-keep-warm",
    service => service.PingDatabaseAsync(CancellationToken.None),
    "*/5 * * * *");

app.Run();

// Needed for integration test WebApplicationFactory
public partial class Program { }

// Hangfire auth filter
public sealed class HangfireAuthFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        // In .NET 10 / newer Hangfire, access HttpContext via the owin environment
        var env = context.GetType()
            .GetProperty("OwinEnvironment")?
            .GetValue(context) as IDictionary<string, object>;

        if (env is not null &&
            env.TryGetValue("server.User", out var userObj) &&
            userObj is System.Security.Claims.ClaimsPrincipal user)
        {
            return user.IsInRole(Roles.Admin);
        }

        // Fallback: allow in development, deny in production
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
    }
}
