using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Application.Authorization;

namespace NyhemsTrafikskola.Infrastructure.Auth;

public sealed class FirstAdminProvisioner
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public FirstAdminProvisioner(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<FirstAdminProvisioningResult> CreateFirstAdminAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(firstName)
            || string.IsNullOrWhiteSpace(lastName))
        {
            return FirstAdminProvisioningResult.Failed("Email, password, first name and last name are required.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (!await _roleManager.RoleExistsAsync(ApplicationRoles.Admin))
        {
            var roleResult = await _roleManager.CreateAsync(new IdentityRole(ApplicationRoles.Admin));
            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return FirstAdminProvisioningResult.Failed(roleResult.Errors.Select(x => x.Description));
            }
        }

        if ((await _userManager.GetUsersInRoleAsync(ApplicationRoles.Admin)).Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FirstAdminProvisioningResult.AlreadyExists();
        }

        var normalizedEmail = email.Trim();
        if (await _userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FirstAdminProvisioningResult.Failed("An account with this email already exists; no role was changed.");
        }

        var user = new ApplicationUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            IsActive = true,
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FirstAdminProvisioningResult.Failed(createResult.Errors.Select(x => x.Description));
        }

        var addRoleResult = await _userManager.AddToRoleAsync(user, ApplicationRoles.Admin);
        if (!addRoleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FirstAdminProvisioningResult.Failed(addRoleResult.Errors.Select(x => x.Description));
        }

        await transaction.CommitAsync(cancellationToken);
        return FirstAdminProvisioningResult.Created();
    }
}

public sealed record FirstAdminProvisioningResult(
    bool WasCreated,
    bool AdminAlreadyExists,
    IReadOnlyCollection<string> Errors)
{
    public static FirstAdminProvisioningResult Created() => new(true, false, Array.Empty<string>());
    public static FirstAdminProvisioningResult AlreadyExists() => new(false, true, Array.Empty<string>());
    public static FirstAdminProvisioningResult Failed(IEnumerable<string> errors) => new(false, false, errors.ToArray());
    public static FirstAdminProvisioningResult Failed(string error) => new(false, false, [error]);
}
