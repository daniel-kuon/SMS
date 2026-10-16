using Microsoft.AspNetCore.Identity;

namespace Sms.Infrastructure;

/// <summary>Identity user; profile data lives in <see cref="Sms.Domain.UserProfile"/> (spec 08).</summary>
public class ApplicationUser : IdentityUser<Guid>;
