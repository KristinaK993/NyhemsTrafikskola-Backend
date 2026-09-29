using Microsoft.AspNetCore.Identity;
using NyhemsTrafikskola.Application.Authorization;

namespace NyhemsTrafikskola.Infrastructure.Auth;

public sealed class RoleInitializer
{
    private readonly RoleManager<IdentityRole> _roleManager;

    public RoleInitializer(RoleManager<IdentityRole> roleManager)
    {
        _roleManager = roleManager;
    }

    public async Task EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = new[]
        {
            ApplicationRoles.Student,
            ApplicationRoles.Instructor,
            ApplicationRoles.Admin,
        };

        foreach (var role in roles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }
}
