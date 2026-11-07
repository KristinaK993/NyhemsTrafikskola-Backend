using NyhemsTrafikskola.Application.Authorization;
using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Tests;

public class StudentAndInstructorRequirementsTests
{
    [Test]
    public void StudentProfile_CanBeCreatedForSelfRegistration_WithoutIntroducingIdentityRoleStateInDomain()
    {
        var student = StudentProfile.CreateSelfRegistration("Anna", "Andersson", "anna@example.com");

        Assert.Multiple(() =>
        {
            Assert.That(student.FirstName, Is.EqualTo("Anna"));
            Assert.That(student.LastName, Is.EqualTo("Andersson"));
            Assert.That(student.Email, Is.EqualTo("anna@example.com"));
        });
    }

    [Test]
    public void StaffRoles_CanRegisterStudents_WithoutSeparatingAdminIntoDomainProfile()
    {
        var instructor = new InstructorProfile("Lena", "Larsson", "lena@example.com");
        var student = StudentProfile.CreateForStaffRegistration("Erik", "Eriksson", "erik@example.com");

        Assert.Multiple(() =>
        {
            Assert.That(instructor.FirstName, Is.EqualTo("Lena"));
            Assert.That(student.FirstName, Is.EqualTo("Erik"));
            Assert.That(student.Email, Is.EqualTo("erik@example.com"));
        });
    }

    [Test]
    public void RegistrationPermissions_UseIdentityRoleCollections_AndAllowMultipleRolesForSameUser()
    {
        var studentRoles = new[] { ApplicationRoles.Student };
        var instructorAndAdminRoles = new[] { ApplicationRoles.Instructor, ApplicationRoles.Admin };

        Assert.Multiple(() =>
        {
            Assert.That(RegistrationPermissions.CanSelfRegister(studentRoles), Is.True);
            Assert.That(RegistrationPermissions.CanSelfRegister([ApplicationRoles.Student, ApplicationRoles.Admin]), Is.False);
            Assert.That(RegistrationPermissions.CanSelfRegister([ApplicationRoles.Student, ApplicationRoles.Instructor]), Is.False);
            Assert.That(RegistrationPermissions.CanSelfRegister(instructorAndAdminRoles), Is.False);
            Assert.That(RegistrationPermissions.CanRegisterStudent(studentRoles), Is.True);
            Assert.That(RegistrationPermissions.CanRegisterStudent(instructorAndAdminRoles), Is.True);
        });
    }

    [Test]
    public void SchedulePermissions_SupportMultipleRoles_AndKeepReadAccessSeparateFromBookingManagement()
    {
        var instructorRoles = new[] { ApplicationRoles.Instructor };
        var adminRoles = new[] { ApplicationRoles.Admin };
        var combinedRoles = new[] { ApplicationRoles.Instructor, ApplicationRoles.Admin };

        Assert.Multiple(() =>
        {
            Assert.That(SchedulePermissions.CanReadAllInstructorSchedules(instructorRoles), Is.True);
            Assert.That(SchedulePermissions.CanReadAllInstructorSchedules(adminRoles), Is.True);
            Assert.That(SchedulePermissions.CanReadAllInstructorSchedules(new[] { ApplicationRoles.Student }), Is.False);

            Assert.That(SchedulePermissions.CanManageOwnBookings(instructorRoles), Is.True);
            Assert.That(SchedulePermissions.CanManageOtherInstructorBookings(instructorRoles), Is.False);
            Assert.That(SchedulePermissions.CanManageOtherInstructorBookings(adminRoles), Is.True);
            Assert.That(SchedulePermissions.CanManageOtherInstructorBookings(combinedRoles), Is.True);
        });
    }
}
