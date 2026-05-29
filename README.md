# Nexum Backend API

Real-time emergency dispatch and safety coordination platform for Redemption City.

## Tech Stack

- **.NET 8** — ASP.NET Core Web API (Modular Monolith)
- **PostgreSQL 16 + PostGIS + pgRouting** — via Supabase
- **ASP.NET Core Identity** — user management and roles
- **SignalR** — real-time WebSocket communication
- **Hangfire** — background job escalation engine
- **Serilog** — structured logging
- **OTP.NET** — password reset OTP generation
- **Firebase Admin SDK** — FCM push notifications
- **MailKit** — SMTP email via Brevo

## Modules

| Module | Description |
|--------|-------------|
| Auth | Registration, login, JWT, OTP password reset |
| Emergency | SOS dispatch, auto-assign, officer tracking |
| Missing Persons | Geofenced broadcast, sighting reporting |
| Parking | Pin car, blocking alerts, auto-escalation |
| Transit | Shuttle dispatch, congestion detection, pgRouting |

## Quick Start

### Prerequisites
- .NET 8 SDK
- PostgreSQL 16 with PostGIS extension (or Supabase account)
- Firebase project with service account JSON

### 1. Clone and configure
```bash
git clone https://github.com/your-org/nexum-backend.git
cd nexum-backend
cp src/Nexum.Api/appsettings.json src/Nexum.Api/appsettings.Development.json
```

Edit `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=YOUR_SUPABASE_HOST;Database=postgres;Username=postgres;Password=YOUR_PASSWORD"
  },
  "Jwt": {
    "Secret": "your-secret-at-least-32-chars",
    "Issuer": "nexum-api",
    "Audience": "nexum-clients"
  }
}
```

Place your Firebase service account JSON at:
```
src/Nexum.Api/firebase-credentials.json
```

### 2. Run migrations
```bash
cd src/Nexum.Api
dotnet ef database update
```

### 3. Run the API
```bash
dotnet run --project src/Nexum.Api
```

API runs at: `https://localhost:7001`  
Swagger UI: `https://localhost:7001/swagger`  
Hangfire dashboard: `https://localhost:7001/hangfire`  
Health live: `https://localhost:7001/health/live`  
Health ready: `https://localhost:7001/health/ready`

### 4. Run tests
```bash
# Unit tests
dotnet test tests/Nexum.UnitTests

# Integration tests
dotnet test tests/Nexum.IntegrationTests
```

## API Overview

### Auth
| Method | Endpoint | Auth |
|--------|----------|------|
| POST | /v1/auth/register | Public |
| POST | /v1/auth/login | Public |
| POST | /v1/auth/refresh | Public |
| POST | /v1/auth/logout | Bearer |
| GET | /v1/auth/me | Bearer |
| PUT | /v1/auth/me/profile | Bearer |
| POST | /v1/auth/forgot-password | Public |
| POST | /v1/auth/verify-otp | Public |
| POST | /v1/auth/reset-password | Public |

### Emergency
| Method | Endpoint | Auth |
|--------|----------|------|
| POST | /v1/emergency/report | Bearer + Geofence |
| GET | /v1/emergency/incidents | Officer/Admin |
| PUT | /v1/emergency/incidents/{id}/status | Officer/Admin |
| PUT | /v1/emergency/officers/availability | Officer |
| PUT | /v1/emergency/officers/location | Officer |

### Missing Persons
| Method | Endpoint | Auth |
|--------|----------|------|
| POST | /v1/alerts/missing-persons | Bearer + Geofence |
| GET | /v1/alerts/missing-persons | Bearer |
| PUT | /v1/alerts/missing-persons/{id}/status | Security/Admin |
| POST | /v1/alerts/missing-persons/{id}/sightings | Bearer + Geofence |

### Parking
| Method | Endpoint | Auth |
|--------|----------|------|
| POST | /v1/parking/pins | Bearer + Geofence |
| GET | /v1/parking/pins/mine | Bearer |
| DELETE | /v1/parking/pins/{id} | Bearer |
| POST | /v1/parking/blocking-alerts | Bearer + Geofence |

### Transit
| Method | Endpoint | Auth |
|--------|----------|------|
| GET | /v1/transit/network/nodes | Public |
| GET | /v1/transit/network/edges | Public |
| POST | /v1/transit/shuttle-requests | Bearer + Geofence |
| GET | /v1/transit/shuttle-requests/mine | Bearer |
| PUT | /v1/transit/drivers/availability | Driver |
| PUT | /v1/transit/drivers/location | Driver |

## SignalR Hubs

| Hub | URL | Events |
|-----|-----|--------|
| EmergencyHub | /hubs/emergency | NewIncidentAlert, IncidentStatusChanged, OfficerLocationUpdate |
| MissingPersonsHub | /hubs/missing-persons | NewMissingPersonAlert, AlertSightingReceived, AlertStatusUpdated |
| ShuttleHub | /hubs/shuttle | NewShuttleRequest, ShuttleAssigned, ShuttleLocationUpdate, CongestionAlert |

## Geofence Headers

All `[RequireInsideCamp]` endpoints require:
```
X-Client-Lat: 6.8403
X-Client-Lng: 3.3864
```

## Identity Roles

| Role | Created by |
|------|-----------|
| worshipper | Self-registration |
| medical_officer | Admin |
| security_officer | Admin |
| driver | Admin |
| admin | Seeded or admin promotion |

## Production Deployment (Ubuntu 22.04)

```bash
# Install .NET 8
wget https://dot.net/v1/dotnet-install.sh && bash dotnet-install.sh --channel 8.0

# Install nginx
sudo apt install nginx certbot python3-certbot-nginx

# Build
dotnet publish src/Nexum.Api -c Release -o /var/www/nexum

# Create systemd service
sudo nano /etc/systemd/system/nexum-api.service
```

```ini
[Unit]
Description=Nexum API
After=network.target

[Service]
WorkingDirectory=/var/www/nexum
ExecStart=/usr/bin/dotnet /var/www/nexum/Nexum.Api.dll
Restart=always
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ConnectionStrings__DefaultConnection=YOUR_CONNECTION_STRING
Environment=Jwt__Secret=YOUR_JWT_SECRET

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable nexum-api
sudo systemctl start nexum-api
sudo certbot --nginx -d api.nexum.ng
```
