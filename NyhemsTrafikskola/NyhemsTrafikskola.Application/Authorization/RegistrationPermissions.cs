namespace NyhemsTrafikskola.Application.Authorization;

public static class RegistrationPermissions
{
    public static bool CanSelfRegister(IEnumerable<string> roles)
    {
        var requestedRoles = roles.ToArray();
        return requestedRoles.Length > 0
            && requestedRoles.All(role => string.Equals(role, ApplicationRoles.Student, StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanRegisterStudent(IEnumerable<string> roles)
    {
        var roleList = roles.ToList();

        return roleList.Contains(ApplicationRoles.Student, StringComparer.OrdinalIgnoreCase)
            || roleList.Contains(ApplicationRoles.Instructor, StringComparer.OrdinalIgnoreCase)
            || roleList.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase);
    }
}
