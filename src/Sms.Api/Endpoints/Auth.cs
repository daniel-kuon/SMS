using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record RegisterRequest(string Email, string Password, string DisplayName, string? Language);
public record LoginRequest(string Email, string Password, bool RememberMe = true);
public record ConfirmEmailRequest(Guid UserId, string Code);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(Guid UserId, string Code, string Password);
public record MeResponse(Guid Id, string Email, bool EmailConfirmed, string DisplayName, string Language, bool IsAdmin, Visibility DefaultContentVisibility, string? AvatarUrl);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");

        g.MapPost("/register", async (RegisterRequest req, UserManager<ApplicationUser> users, SmsDbContext db, MailSender mail, HttpRequest http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.DisplayName)) return Invalid("displayName", "Anzeigename fehlt.");
            var user = new ApplicationUser { UserName = req.Email, Email = req.Email };
            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded) return TypedResults.ValidationProblem(result.Errors.GroupBy(e => e.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
            db.Profiles.Add(new UserProfile { UserId = user.Id, DisplayName = req.DisplayName.Trim(), Language = req.Language is "en" ? "en" : "de" });
            await db.SaveChangesAsync(ct);
            await SendConfirmationAsync(user, users, mail, http, cfg, ct);
            return Results.Ok(new { user.Id, user.Email });
        });

        g.MapPost("/login", async (LoginRequest req, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null) return Results.Problem(statusCode: 401, title: "E-Mail oder Passwort falsch.");
            var result = await signIn.PasswordSignInAsync(user, req.Password, req.RememberMe, lockoutOnFailure: true);
            if (result.IsNotAllowed) return Results.Problem(statusCode: 403, title: "E-Mail-Adresse noch nicht bestätigt.");
            if (result.IsLockedOut) return Results.Problem(statusCode: 423, title: "Konto vorübergehend gesperrt.");
            if (!result.Succeeded) return Results.Problem(statusCode: 401, title: "E-Mail oder Passwort falsch.");
            return Results.Ok();
        });

        g.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); })
            .RequireAuthorization();

        g.MapPost("/confirm-email", async (ConfirmEmailRequest req, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(req.UserId.ToString());
            if (user is null) return Results.NotFound();
            var code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Code));
            var result = await users.ConfirmEmailAsync(user, code);
            return result.Succeeded ? Results.Ok() : Results.Problem(statusCode: 400, title: "Ungültiger Bestätigungscode.");
        });

        g.MapPost("/resend-confirmation", async (ForgotPasswordRequest req, UserManager<ApplicationUser> users, MailSender mail, HttpRequest http, IConfiguration cfg, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is { EmailConfirmed: false }) await SendConfirmationAsync(user, users, mail, http, cfg, ct);
            return Results.Ok();
        });

        g.MapPost("/forgot-password", async (ForgotPasswordRequest req, UserManager<ApplicationUser> users, MailSender mail, HttpRequest http, IConfiguration cfg, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is not null)
            {
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
                var link = $"{WebBaseUrl(http, cfg)}/auth/reset-password?userId={user.Id}&code={code}";
                await mail.SendAsync(user.Email!, "Passwort zurücksetzen", $"<p>Neues Passwort setzen: <a href=\"{link}\">{link}</a></p>", ct);
            }
            return Results.Ok();
        });

        g.MapPost("/reset-password", async (ResetPasswordRequest req, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(req.UserId.ToString());
            if (user is null) return Results.NotFound();
            var code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Code));
            var result = await users.ResetPasswordAsync(user, code, req.Password);
            return result.Succeeded ? Results.Ok() : TypedResults.ValidationProblem(result.Errors.GroupBy(e => e.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
        });

        g.MapGet("/me", async (CurrentUser current, UserManager<ApplicationUser> users, SmsDbContext db, CancellationToken ct) =>
        {
            if (current.Id is not { } id) return Results.Unauthorized();
            var user = await users.FindByIdAsync(id.ToString());
            var profile = await current.ProfileAsync(ct);
            if (user is null || profile is null) return Results.Unauthorized();
            return Results.Ok(new MeResponse(user.Id, user.Email!, user.EmailConfirmed, profile.DisplayName, profile.Language, profile.IsAdmin, profile.DefaultContentVisibility,
                profile.AvatarMediaId is { } a ? $"/api/media/{a}/thumb" : null));
        });
    }

    private static async Task SendConfirmationAsync(ApplicationUser user, UserManager<ApplicationUser> users, MailSender mail, HttpRequest http, IConfiguration cfg, CancellationToken ct)
    {
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        var link = $"{WebBaseUrl(http, cfg)}/auth/confirm?userId={user.Id}&code={code}";
        await mail.SendAsync(user.Email!, "E-Mail-Adresse bestätigen", $"<p>Willkommen an Bord! Bitte bestätige deine E-Mail-Adresse: <a href=\"{link}\">{link}</a></p>", ct);
    }
}
