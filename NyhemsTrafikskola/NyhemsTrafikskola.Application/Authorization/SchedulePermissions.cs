namespace NyhemsTrafikskola.Application.Authorization;

public static class SchedulePermissions
{
    public static bool CanReadAllInstructorSchedules(IEnumerable<string> roles)
    {
        var roleList = roles.ToList();

        return roleList.Contains(ApplicationRoles.Instructor, StringComparer.OrdinalIgnoreCase)
            || roleList.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanManageOwnBookings(IEnumerable<string> roles)
    {
        return roles.Contains(ApplicationRoles.Instructor, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanManageOtherInstructorBookings(IEnumerable<string> roles)
    {
        return roles.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase);
    }
}
