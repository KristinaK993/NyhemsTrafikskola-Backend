namespace NyhemsTrafikskola.Application.Authorization;

public static class RegistrationPermissions
{
    public static bool CanSelfRegister(IEnumerable<string> roles)
    {
        return roles.Contains(ApplicationRoles.Student, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanRegisterStudent(IEnumerable<string> roles)
    {
        var roleList = roles.ToList();

        return roleList.Contains(ApplicationRoles.Student, StringComparer.OrdinalIgnoreCase)
            || roleList.Contains(ApplicationRoles.Instructor, StringComparer.OrdinalIgnoreCase)
            || roleList.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase);
    }
}
