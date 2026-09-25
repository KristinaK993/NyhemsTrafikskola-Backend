namespace NyhemsTrafikskola.Domain;

public class StudentProfile : PersonProfile
{
    public StudentProfile(string firstName, string lastName, string email)
        : base(firstName, lastName, email)
    {
    }

    public static StudentProfile CreateSelfRegistration(string firstName, string lastName, string email)
    {
        return new StudentProfile(firstName, lastName, email);
    }

    public static StudentProfile CreateForStaffRegistration(string firstName, string lastName, string email)
    {
        return new StudentProfile(firstName, lastName, email);
    }
}
