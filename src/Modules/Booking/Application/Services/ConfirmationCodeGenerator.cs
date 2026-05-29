using System.Security.Cryptography;

namespace Nexum.Modules.Booking.Application.Services;

/// <summary>
/// Generates 8-character alphanumeric confirmation codes.
/// Excludes visually ambiguous characters: O, 0, I, 1
/// Example output: A3FX92KL
/// </summary>
public static class ConfirmationCodeGenerator
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        return new string(bytes.Select(b => Chars[b % Chars.Length]).ToArray());
    }
}
